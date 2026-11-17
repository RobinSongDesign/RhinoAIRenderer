using System;
using System.Collections.Generic;
using Chameleon.Core.Generation;

namespace Chameleon.Core.Jobs
{
    /// <summary>Runs generation jobs with per-backend concurrency limits, dependencies and cancellation.</summary>
    public interface IGenerationQueue
    {
        /// <summary>Raised on a thread-pool thread, in order, each time a job is created or changes state.</summary>
        event EventHandler<JobChangedEventArgs>? JobChanged;

        /// <summary>Adds a job and returns its initial snapshot.</summary>
        GenerationJob Enqueue(string backendId, GenerationRequest request, JobOptions? options = null);

        /// <summary>Adds all jobs of a plan atomically, wiring up their mutual dependencies.</summary>
        /// <param name="backendId">Backend that runs the batch.</param>
        /// <param name="plan">Plan produced by <see cref="BatchPlanner"/>.</param>
        /// <param name="groupId">Group id shared by all jobs of the batch.</param>
        /// <param name="maxConsistencyRefs">Cap on automatically attached consistency references per job.</param>
        IReadOnlyList<GenerationJob> EnqueueBatch(
            string backendId,
            IReadOnlyList<PlannedJob> plan,
            string groupId,
            int maxConsistencyRefs = JobOptions.DefaultMaxConsistencyRefs);

        /// <summary>Cancels a job. Queued jobs end immediately; running jobs have their request aborted.</summary>
        void Cancel(string jobId);

        /// <summary>Cancels every unfinished job of a group.</summary>
        void CancelGroup(string groupId);

        /// <summary>Returns the snapshot of one job, or null when the id is unknown.</summary>
        GenerationJob? Find(string jobId);

        /// <summary>Returns snapshots of all retained jobs, oldest first.</summary>
        IReadOnlyList<GenerationJob> Snapshot();
    }
}
