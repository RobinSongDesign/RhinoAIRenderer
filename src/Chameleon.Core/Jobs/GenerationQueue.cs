using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;

namespace Chameleon.Core.Jobs
{
    /// <summary>In-process implementation of <see cref="IGenerationQueue"/>.</summary>
    public sealed class GenerationQueue : IGenerationQueue, IDisposable
    {
        /// <summary>Default number of jobs kept for inspection.</summary>
        public const int DefaultMaxRetainedJobs = 200;

        private readonly Func<string, IRenderBackend?> _resolveBackend;
        private readonly int _maxRetainedJobs;
        private readonly object _gate = new object();
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Dictionary<string, Entry> _byId = new Dictionary<string, Entry>(StringComparer.Ordinal);

        // Change notifications are queued and drained by one thread at a time, so subscribers see
        // them in the order the changes happened even though jobs finish on arbitrary threads.
        private readonly Queue<GenerationJob> _pendingEvents = new Queue<GenerationJob>();
        private bool _drainingEvents;
        private bool _disposed;

        /// <summary>Creates a queue.</summary>
        /// <param name="resolveBackend">Maps a backend id to a backend; returns null for unknown ids.</param>
        /// <param name="maxRetainedJobs">How many jobs to keep; the oldest finished ones are dropped first.</param>
        public GenerationQueue(Func<string, IRenderBackend?> resolveBackend, int maxRetainedJobs = DefaultMaxRetainedJobs)
        {
            _resolveBackend = resolveBackend ?? throw new ArgumentNullException(nameof(resolveBackend));
            _maxRetainedJobs = Math.Max(1, maxRetainedJobs);
        }

        /// <inheritdoc />
        public event EventHandler<JobChangedEventArgs>? JobChanged;

        /// <inheritdoc />
        public GenerationJob Enqueue(string backendId, GenerationRequest request, JobOptions? options = null)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            options = options ?? new JobOptions();
            GenerationJob snapshot;
            lock (_gate)
            {
                ThrowIfDisposed();
                Entry entry = AddEntry(backendId, request, options.GroupId, options.Label, options.DependsOn, options.MaxConsistencyRefs, options.Tags);
                snapshot = entry.ToSnapshot();
                Pump();
            }

            DrainEvents();
            return snapshot;
        }

        /// <inheritdoc />
        public IReadOnlyList<GenerationJob> EnqueueBatch(
            string backendId,
            IReadOnlyList<PlannedJob> plan,
            string groupId,
            int maxConsistencyRefs = JobOptions.DefaultMaxConsistencyRefs)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var snapshots = new List<GenerationJob>(plan.Count);
            lock (_gate)
            {
                ThrowIfDisposed();
                var created = new List<Entry>(plan.Count);
                foreach (PlannedJob planned in plan)
                {
                    // Plan entries may only point backwards, which also rules out cycles.
                    List<string> dependsOn = planned.DependsOnIndexes
                        .Where(index => index >= 0 && index < created.Count)
                        .Select(index => created[index].Id)
                        .ToList();
                    created.Add(AddEntry(backendId, planned.Request, groupId, planned.Label, dependsOn, maxConsistencyRefs, planned.Tags));
                }

                snapshots.AddRange(created.Select(e => e.ToSnapshot()));
                Pump();
            }

            DrainEvents();
            return snapshots;
        }

        /// <inheritdoc />
        public void Cancel(string jobId)
        {
            lock (_gate)
            {
                if (_byId.TryGetValue(jobId ?? string.Empty, out Entry? entry))
                {
                    CancelEntry(entry);
                    Pump();
                }
            }

            DrainEvents();
        }

        /// <inheritdoc />
        public void CancelGroup(string groupId)
        {
            lock (_gate)
            {
                foreach (Entry entry in _entries.Where(e => e.GroupId != null && e.GroupId == groupId).ToList())
                {
                    CancelEntry(entry);
                }

                Pump();
            }

            DrainEvents();
        }

        /// <inheritdoc />
        public GenerationJob? Find(string jobId)
        {
            lock (_gate)
            {
                return _byId.TryGetValue(jobId ?? string.Empty, out Entry? entry) ? entry.ToSnapshot() : null;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<GenerationJob> Snapshot()
        {
            lock (_gate)
            {
                return _entries.Select(e => e.ToSnapshot()).ToList();
            }
        }

        /// <summary>Cancels every unfinished job and stops accepting new ones.</summary>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                foreach (Entry entry in _entries.ToList())
                {
                    CancelEntry(entry);
                }
            }

            DrainEvents();
        }

        // ── Scheduling (all called with _gate held) ────────────────────────────

        private Entry AddEntry(
            string backendId,
            GenerationRequest request,
            string? groupId,
            string? label,
            IEnumerable<string> dependsOn,
            int maxConsistencyRefs,
            IEnumerable<KeyValuePair<string, string>> tags)
        {
            var entry = new Entry(
                Guid.NewGuid().ToString("N"),
                backendId ?? string.Empty,
                request.Clone(),
                groupId,
                label,
                dependsOn.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList(),
                Math.Max(0, maxConsistencyRefs),
                tags.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal));

            _entries.Add(entry);
            _byId[entry.Id] = entry;
            Trim();
            Publish(entry);
            return entry;
        }

        private void CancelEntry(Entry entry)
        {
            if (entry.State == JobState.Queued)
            {
                entry.State = JobState.Cancelled;
                entry.FinishedAt = DateTimeOffset.UtcNow;
                Publish(entry);
            }
            else if (entry.State == JobState.Running)
            {
                // The state changes when the backend call unwinds.
                entry.Cancellation?.Cancel();
            }
        }

        private void Pump()
        {
            if (_disposed)
            {
                return;
            }

            foreach (Entry entry in _entries.Where(e => e.State == JobState.Queued).ToList())
            {
                if (!DependenciesFinished(entry))
                {
                    continue;
                }

                IRenderBackend? backend = _resolveBackend(entry.BackendId);
                if (backend == null)
                {
                    entry.State = JobState.Failed;
                    entry.Error = new RenderException(
                        RenderErrorCode.InvalidRequest,
                        "Unknown backend '" + entry.BackendId + "'.");
                    entry.FinishedAt = DateTimeOffset.UtcNow;
                    Publish(entry);
                    continue;
                }

                BackendCapabilities capabilities = backend.GetCapabilities(entry.Request.ModelId);
                int limit = Math.Max(1, capabilities.MaxConcurrency);
                int running = _entries.Count(e => e.State == JobState.Running && e.BackendId == entry.BackendId);
                if (running >= limit)
                {
                    continue;
                }

                GenerationRequest effective = BuildEffectiveRequest(entry, capabilities);
                entry.State = JobState.Running;
                entry.StartedAt = DateTimeOffset.UtcNow;
                entry.Cancellation = new CancellationTokenSource();
                Publish(entry);

                CancellationToken token = entry.Cancellation.Token;
                Task.Run(() => RunAsync(entry, backend, effective, token));
            }
        }

        private bool DependenciesFinished(Entry entry)
        {
            foreach (string id in entry.DependsOn)
            {
                // A dependency that was trimmed or never existed cannot block the job forever.
                if (_byId.TryGetValue(id, out Entry? dependency) && !dependency.IsFinished)
                {
                    return false;
                }
            }

            return true;
        }

        private GenerationRequest BuildEffectiveRequest(Entry entry, BackendCapabilities capabilities)
        {
            GenerationRequest effective = entry.Request.Clone();

            // Only results of dependencies that succeeded take part; a failed view must not
            // pollute the views rendered after it.
            var consistency = new List<ReferenceImage>();
            foreach (string id in entry.DependsOn)
            {
                if (_byId.TryGetValue(id, out Entry? dependency) &&
                    dependency.State == JobState.Succeeded &&
                    dependency.Result != null &&
                    dependency.Result.Images.Count > 0)
                {
                    consistency.Add(new ReferenceImage(dependency.Result.Images[0], ReferenceRole.Consistency));
                }
            }

            int allowed = entry.MaxConsistencyRefs;
            if (capabilities.MaxInputImages > 0)
            {
                // References the user attached are never dropped; only automatic ones give way.
                allowed = Math.Min(allowed, Math.Max(0, capabilities.MaxInputImages - effective.InputImageCount));
            }

            // Keep the most recent results: they are closest to the view being rendered.
            foreach (ReferenceImage reference in consistency.Skip(Math.Max(0, consistency.Count - allowed)))
            {
                effective.References.Add(reference);
            }

            return effective;
        }

        private async Task RunAsync(Entry entry, IRenderBackend backend, GenerationRequest request, CancellationToken token)
        {
            GenerationResult? result = null;
            RenderException? error = null;
            bool cancelled = false;

            try
            {
                result = await backend.GenerateAsync(request, null, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (RenderException ex)
            {
                error = ex;
            }
            catch (Exception ex)
            {
                // A backend bug must fail one job, not take the queue down.
                error = new RenderException(RenderErrorCode.Unknown, ex.Message, null, null, ex);
            }

            lock (_gate)
            {
                entry.FinishedAt = DateTimeOffset.UtcNow;
                entry.Cancellation?.Dispose();
                entry.Cancellation = null;

                if (cancelled)
                {
                    entry.State = JobState.Cancelled;
                }
                else if (error != null)
                {
                    entry.State = JobState.Failed;
                    entry.Error = error;
                }
                else
                {
                    entry.State = JobState.Succeeded;
                    entry.Result = result;
                }

                Publish(entry);
                Pump();
            }

            DrainEvents();
        }

        private void Trim()
        {
            while (_entries.Count > _maxRetainedJobs)
            {
                Entry? oldestFinished = _entries.FirstOrDefault(e => e.IsFinished);
                if (oldestFinished == null)
                {
                    return;
                }

                _entries.Remove(oldestFinished);
                _byId.Remove(oldestFinished.Id);
            }
        }

        private void Publish(Entry entry)
        {
            _pendingEvents.Enqueue(entry.ToSnapshot());
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GenerationQueue));
            }
        }

        // ── Event delivery (called without _gate held) ─────────────────────────

        private void DrainEvents()
        {
            lock (_gate)
            {
                if (_drainingEvents)
                {
                    return;
                }

                _drainingEvents = true;
            }

            while (true)
            {
                GenerationJob snapshot;
                lock (_gate)
                {
                    if (_pendingEvents.Count == 0)
                    {
                        _drainingEvents = false;
                        return;
                    }

                    snapshot = _pendingEvents.Dequeue();
                }

                try
                {
                    JobChanged?.Invoke(this, new JobChangedEventArgs(snapshot));
                }
                catch (Exception)
                {
                    // A faulty subscriber must not stop delivery to the others or stall the queue.
                }
            }
        }

        /// <summary>Mutable state of one job; only touched with the queue lock held.</summary>
        private sealed class Entry
        {
            public Entry(
                string id,
                string backendId,
                GenerationRequest request,
                string? groupId,
                string? label,
                List<string> dependsOn,
                int maxConsistencyRefs,
                Dictionary<string, string> tags)
            {
                Id = id;
                BackendId = backendId;
                Request = request;
                GroupId = groupId;
                Label = label;
                DependsOn = dependsOn;
                MaxConsistencyRefs = maxConsistencyRefs;
                Tags = tags;
                CreatedAt = DateTimeOffset.UtcNow;
            }

            public string Id { get; }

            public string BackendId { get; }

            public GenerationRequest Request { get; }

            public string? GroupId { get; }

            public string? Label { get; }

            public List<string> DependsOn { get; }

            public int MaxConsistencyRefs { get; }

            public Dictionary<string, string> Tags { get; }

            public DateTimeOffset CreatedAt { get; }

            public JobState State { get; set; } = JobState.Queued;

            public GenerationResult? Result { get; set; }

            public RenderException? Error { get; set; }

            public DateTimeOffset? StartedAt { get; set; }

            public DateTimeOffset? FinishedAt { get; set; }

            public CancellationTokenSource? Cancellation { get; set; }

            public bool IsFinished =>
                State == JobState.Succeeded || State == JobState.Failed || State == JobState.Cancelled;

            public GenerationJob ToSnapshot()
            {
                return new GenerationJob(
                    Id, BackendId, Request, State, Result, Error, GroupId, Label,
                    DependsOn.ToArray(), new Dictionary<string, string>(Tags, StringComparer.Ordinal),
                    CreatedAt, StartedAt, FinishedAt);
            }
        }
    }
}
