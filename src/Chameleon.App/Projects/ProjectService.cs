using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Assets;
using Chameleon.App.Bridge;
using Chameleon.Core.Imaging;
using Chameleon.Core.Sessions;

namespace Chameleon.App.Projects
{
    /// <summary>
    /// The edit histories of this Rhino session: the current project (that of the active document,
    /// or a temporary one until a document is bound) plus any project opened earlier, which jobs
    /// submitted from it still write into. Thread-safe.
    /// </summary>
    public sealed class ProjectService : IProjectHistory, IDisposable
    {
        private readonly IProjectStore _store;
        private readonly IAssetStore _cache;
        private readonly Func<DateTimeOffset> _clock;
        private readonly object _gate = new object();
        private readonly Dictionary<string, OpenProject> _projects = new Dictionary<string, OpenProject>(StringComparer.Ordinal);
        private string _currentId;

        /// <summary>Creates the service with an empty temporary project as the current one.</summary>
        /// <param name="store">Project storage.</param>
        /// <param name="cache">Working assets (captures, uploads, results) that nodes copy their images from.</param>
        /// <param name="clock">Time source for node timestamps.</param>
        public ProjectService(IProjectStore store, IAssetStore cache, Func<DateTimeOffset>? clock = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _currentId = ProjectIds.NewTemporary();
            _projects[_currentId] = new OpenProject(new EditSession(_currentId), _store.GetAssets(_currentId));
        }

        /// <summary>Raised after a project's nodes or selection changed, or when another project became current.</summary>
        public event EventHandler<ProjectChangedEventArgs>? Changed;

        /// <inheritdoc />
        public string CurrentProjectId
        {
            get
            {
                lock (_gate)
                {
                    return _currentId;
                }
            }
        }

        /// <summary>Makes a project current, loading it or creating it empty when it does not exist.</summary>
        public async Task OpenProjectAsync(string projectId, CancellationToken cancellationToken)
        {
            ProjectIds.Require(projectId);
            bool loaded;
            lock (_gate)
            {
                loaded = _projects.ContainsKey(projectId);
            }

            if (!loaded)
            {
                SessionSnapshot? snapshot = await _store.LoadAsync(projectId, cancellationToken).ConfigureAwait(false);
                // The file's own id is ignored: the directory decides which project this is.
                EditSession session = snapshot == null
                    ? new EditSession(projectId)
                    : EditSession.FromSnapshot(new SessionSnapshot { ProjectId = projectId, CurrentNodeId = snapshot.CurrentNodeId, Nodes = snapshot.Nodes });
                lock (_gate)
                {
                    if (!_projects.ContainsKey(projectId))
                    {
                        _projects[projectId] = new OpenProject(session, _store.GetAssets(projectId));
                    }
                }
            }

            lock (_gate)
            {
                _currentId = projectId;
            }

            OnChanged(projectId);
        }

        /// <summary>A copy of the current project's history.</summary>
        public SessionSnapshot GetCurrent()
        {
            lock (_gate)
            {
                return Current.Session.ToSnapshot();
            }
        }

        /// <summary>Adds a node to the current project.</summary>
        public ImageNode AddNode(NodeSpec spec)
        {
            return AddNode(CurrentProjectId, spec);
        }

        /// <inheritdoc />
        public ImageNode AddNode(string projectId, NodeSpec spec, IEnumerable<ImageData>? images = null)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            ImageNode added;
            lock (_gate)
            {
                OpenProject project = Require(projectId);
                if (spec.ParentId != null && project.Session.Find(spec.ParentId) == null)
                {
                    throw BridgeException.NotFound("Unknown parent node '" + spec.ParentId + "'.");
                }

                var node = new ImageNode
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ParentId = spec.ParentId,
                    Kind = spec.Kind,
                    AssetId = spec.AssetId,
                    CreatedAt = _clock(),
                    Label = string.IsNullOrEmpty(spec.Label) ? null : spec.Label,
                    Recipe = spec.Recipe?.Clone(),
                    Camera = spec.Camera?.Clone()
                };

                // Copy first: a node must never point at an image the project does not hold.
                CopyAssets(project, node.GetAssetIds(), images);
                added = project.Session.AddNode(node).Clone();
                Save(project);
            }

            OnChanged(projectId);
            return added;
        }

        /// <inheritdoc />
        public bool Contains(string projectId, string nodeId)
        {
            lock (_gate)
            {
                return _projects.TryGetValue(projectId, out OpenProject? project) && project.Session.Find(nodeId) != null;
            }
        }

        /// <summary>Changes a node of the current project.</summary>
        public ImageNode UpdateNode(string nodeId, string? label, bool? starred)
        {
            return Mutate(project => project.Session.UpdateNode(nodeId, label, starred).Clone());
        }

        /// <summary>Removes a node of the current project, and the images nothing else uses.</summary>
        public void DeleteNode(string nodeId, bool withDescendants)
        {
            Mutate(project =>
            {
                RemoveUnusedAssets(project, project.Session.DeleteNode(nodeId, withDescendants));
                return true;
            });
        }

        /// <summary>Selects a node of the current project (null clears the selection).</summary>
        public void SetCurrent(string? nodeId)
        {
            Mutate(project =>
            {
                project.Session.SetCurrent(nodeId);
                return true;
            });
        }

        /// <summary>Removes every node of the current project and its images.</summary>
        public void Clear()
        {
            Mutate(project =>
            {
                RemoveUnusedAssets(project, project.Session.Clear());
                return true;
            });
        }

        /// <summary>
        /// Finds an image in the open projects (the current one first). Project images outlive the
        /// working cache, so they are looked up before it.
        /// </summary>
        public ImageData? TryGetAsset(string assetId)
        {
            List<OpenProject> projects;
            lock (_gate)
            {
                projects = _projects.Values.OrderByDescending(p => p.Session.ProjectId == _currentId).ToList();
            }

            foreach (OpenProject project in projects)
            {
                ImageData? image = project.Assets.TryGet(assetId);
                if (image != null)
                {
                    return image;
                }
            }

            return null;
        }

        /// <summary>
        /// Removes temporary projects older than <paramref name="age"/> that earlier sessions left
        /// behind (after a crash, for instance). Recent ones may belong to another running Rhino.
        /// </summary>
        public int PurgeAbandonedTemporaryProjects(TimeSpan age)
        {
            DateTime cutoff = DateTime.UtcNow - age;
            int removed = 0;
            foreach (KeyValuePair<string, DateTime> entry in _store.List())
            {
                bool open;
                lock (_gate)
                {
                    open = _projects.ContainsKey(entry.Key);
                }

                if (ProjectIds.IsTemporary(entry.Key) && entry.Value < cutoff && !open)
                {
                    _store.DeleteProject(entry.Key);
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>Writes pending changes and removes this session's temporary projects.</summary>
        public void Dispose()
        {
            List<string> temporary;
            lock (_gate)
            {
                temporary = _projects.Keys.Where(ProjectIds.IsTemporary).ToList();
            }

            foreach (string projectId in temporary)
            {
                _store.DeleteProject(projectId);
            }

            (_store as IDisposable)?.Dispose();
        }

        private T Mutate<T>(Func<OpenProject, T> change)
        {
            T result;
            string projectId;
            lock (_gate)
            {
                OpenProject project = Current;
                projectId = project.Session.ProjectId;
                result = change(project);
                Save(project);
            }

            OnChanged(projectId);
            return result;
        }

        private void CopyAssets(OpenProject project, IEnumerable<string> assetIds, IEnumerable<ImageData>? images)
        {
            Dictionary<string, ImageData> supplied = (images ?? Enumerable.Empty<ImageData>())
                .GroupBy(i => i.Sha256Hex, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (string assetId in assetIds.Distinct(StringComparer.Ordinal))
            {
                if (project.Assets.Contains(assetId))
                {
                    continue;
                }

                ImageData image = (supplied.TryGetValue(assetId, out ImageData? given) ? given : null)
                                  ?? _cache.TryGet(assetId)
                                  ?? FindInOtherProjects(project, assetId)
                                  ?? throw BridgeException.NotFound("Unknown asset '" + assetId + "'.");
                project.Assets.Put(image);
            }
        }

        private ImageData? FindInOtherProjects(OpenProject except, string assetId)
        {
            foreach (OpenProject other in _projects.Values)
            {
                if (!ReferenceEquals(other, except))
                {
                    ImageData? image = other.Assets.TryGet(assetId);
                    if (image != null)
                    {
                        return image;
                    }
                }
            }

            return null;
        }

        private static void RemoveUnusedAssets(OpenProject project, IEnumerable<ImageNode> removed)
        {
            ISet<string> stillUsed = project.Session.GetReferencedAssetIds();
            foreach (string assetId in removed.SelectMany(n => n.GetAssetIds()).Distinct(StringComparer.Ordinal))
            {
                if (!stillUsed.Contains(assetId))
                {
                    project.Assets.Delete(assetId);
                }
            }
        }

        private void Save(OpenProject project)
        {
            _store.ScheduleSave(project.Session.ToSnapshot());
        }

        private OpenProject Current => _projects[_currentId];

        private OpenProject Require(string projectId)
        {
            return _projects.TryGetValue(projectId, out OpenProject? project)
                ? project
                : throw BridgeException.NotFound("Project '" + projectId + "' is not open.");
        }

        private void OnChanged(string projectId)
        {
            Changed?.Invoke(this, new ProjectChangedEventArgs(projectId));
        }

        /// <summary>A loaded project: its history and its image folder.</summary>
        private sealed class OpenProject
        {
            public OpenProject(EditSession session, DiskAssetStore assets)
            {
                Session = session;
                Assets = assets;
            }

            public EditSession Session { get; }

            public DiskAssetStore Assets { get; }
        }
    }
}
