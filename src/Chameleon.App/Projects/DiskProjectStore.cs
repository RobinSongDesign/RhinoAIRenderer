using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Assets;
using Chameleon.Core.Serialization;
using Chameleon.Core.Sessions;
using Newtonsoft.Json;

namespace Chameleon.App.Projects
{
    /// <summary>
    /// Projects on disk: <c>{root}/{projectId}/project.json</c> plus <c>assets/</c>. Saves are
    /// debounced and written atomically (temporary file, then replace), so a crash leaves either the
    /// old or the new file, never a partial one.
    /// </summary>
    public sealed class DiskProjectStore : IProjectStore, IDisposable
    {
        /// <summary>Default wait before a scheduled save is written.</summary>
        public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(500);

        private const string ProjectFileName = "project.json";

        private readonly string _root;
        private readonly TimeSpan _debounce;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly Action<string> _log;
        private readonly object _gate = new object();

        // Files are written synchronously on a pool thread; this lock keeps writers of the same
        // directory tree (scheduled saves, flushes, deletions) from interleaving.
        private readonly object _fileLock = new object();
        private readonly Dictionary<string, string> _pending = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Writer> _writers = new Dictionary<string, Writer>(StringComparer.Ordinal);

        /// <summary>Creates a store under a root directory.</summary>
        /// <param name="root">Directory holding one sub-directory per project.</param>
        /// <param name="log">Receives write failures.</param>
        /// <param name="debounce">Wait before writing; defaults to <see cref="DefaultDebounce"/>.</param>
        /// <param name="delay">Delay function, replaceable in tests.</param>
        public DiskProjectStore(string root, Action<string>? log = null, TimeSpan? debounce = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _log = log ?? (_ => { });
            _debounce = debounce ?? DefaultDebounce;
            _delay = delay ?? Task.Delay;
            Directory.CreateDirectory(_root);
        }

        /// <inheritdoc />
        public async Task<SessionSnapshot?> LoadAsync(string projectId, CancellationToken cancellationToken)
        {
            ProjectIds.Require(projectId);
            string? json;
            lock (_gate)
            {
                _pending.TryGetValue(projectId, out json);
            }

            if (json == null)
            {
                string path = ProjectFile(projectId);
                if (!File.Exists(path))
                {
                    return null;
                }

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    json = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            try
            {
                return JsonConvert.DeserializeObject<SessionSnapshot>(json, CoreJson.Settings);
            }
            catch (JsonException ex)
            {
                // An unreadable project must not stop the app; its images are still on disk.
                _log("Project '" + projectId + "' could not be read and starts empty: " + ex.Message);
                return new SessionSnapshot { ProjectId = projectId };
            }
        }

        /// <inheritdoc />
        public void ScheduleSave(SessionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            string projectId = ProjectIds.Require(snapshot.ProjectId);
            string json = JsonConvert.SerializeObject(snapshot, Formatting.Indented, CoreJson.Settings);
            lock (_gate)
            {
                _pending[projectId] = json;
                if (!_writers.ContainsKey(projectId))
                {
                    var writer = new Writer();
                    _writers[projectId] = writer;
                    writer.Task = Task.Run(() => RunWriterAsync(projectId, writer.Skip.Token));
                }
            }
        }

        /// <inheritdoc />
        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            Task[] writers;
            lock (_gate)
            {
                writers = _writers.Values.Select(w => w.Task).ToArray();
                SkipDelaysLocked();
            }

            await Task.WhenAll(writers).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <inheritdoc />
        public DiskAssetStore GetAssets(string projectId)
        {
            return new DiskAssetStore(Path.Combine(ProjectDirectory(projectId), "assets"));
        }

        /// <inheritdoc />
        public void DeleteProject(string projectId)
        {
            ProjectIds.Require(projectId);
            lock (_gate)
            {
                _pending.Remove(projectId);
            }

            lock (_fileLock)
            {
                try
                {
                    string directory = ProjectDirectory(projectId);
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
                catch (IOException ex)
                {
                    _log("Removing project '" + projectId + "' failed: " + ex.Message);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _log("Removing project '" + projectId + "' failed: " + ex.Message);
                }
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<KeyValuePair<string, DateTime>> List()
        {
            return Directory.GetDirectories(_root)
                .Select(d => new KeyValuePair<string, DateTime>(Path.GetFileName(d), Directory.GetLastWriteTimeUtc(d)))
                .Where(p => ProjectIds.IsValid(p.Key))
                .ToList();
        }

        /// <summary>
        /// Writes everything scheduled, synchronously. For shutdown, where nothing may be awaited;
        /// elsewhere use <see cref="FlushAsync"/>.
        /// </summary>
        public void FlushNow()
        {
            List<KeyValuePair<string, string>> pending;
            lock (_gate)
            {
                pending = _pending.ToList();
                _pending.Clear();
                SkipDelaysLocked();
            }

            foreach (KeyValuePair<string, string> item in pending)
            {
                Write(item.Key, item.Value);
            }
        }

        /// <summary>Writes what is scheduled and stops.</summary>
        public void Dispose()
        {
            FlushNow();
        }

        // A flushed writer writes what remains without waiting and then ends; the next save
        // starts a new, debounced writer.
        private void SkipDelaysLocked()
        {
            foreach (Writer writer in _writers.Values)
            {
                writer.Skip.Cancel();
            }
        }

        private async Task RunWriterAsync(string projectId, CancellationToken skip)
        {
            while (true)
            {
                try
                {
                    await _delay(_debounce, skip).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // A flush asked for the write to happen now.
                }

                string? json;
                lock (_gate)
                {
                    if (!_pending.TryGetValue(projectId, out json))
                    {
                        _writers[projectId].Skip.Dispose();
                        _writers.Remove(projectId);
                        return;
                    }

                    _pending.Remove(projectId);
                }

                Write(projectId, json);
            }
        }

        private void Write(string projectId, string json)
        {
            lock (_fileLock)
            {
                string path = ProjectFile(projectId);
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(temp, json, new UTF8Encoding(false));
                    if (File.Exists(path))
                    {
                        File.Replace(temp, path, null);
                    }
                    else
                    {
                        File.Move(temp, path);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _log("Saving project '" + projectId + "' failed: " + ex.Message);
                    TryDelete(temp);
                }
            }
        }

        /// <summary>The background task writing one project, and the switch that cuts its wait short.</summary>
        private sealed class Writer
        {
            public CancellationTokenSource Skip { get; } = new CancellationTokenSource();

            public Task Task { get; set; } = Task.CompletedTask;
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private string ProjectDirectory(string projectId)
        {
            return Path.Combine(_root, ProjectIds.Require(projectId));
        }

        private string ProjectFile(string projectId)
        {
            return Path.Combine(ProjectDirectory(projectId), ProjectFileName);
        }
    }
}
