using System;
using System.Collections.Generic;
using Chameleon.Core.Generation;

namespace Chameleon.Core.Jobs
{
    /// <summary>An immutable snapshot of a job at one point in time.</summary>
    public sealed class GenerationJob
    {
        internal GenerationJob(
            string id,
            string backendId,
            GenerationRequest request,
            JobState state,
            GenerationResult? result,
            RenderException? error,
            string? groupId,
            string? label,
            IReadOnlyList<string> dependsOn,
            IReadOnlyDictionary<string, string> tags,
            DateTimeOffset createdAt,
            DateTimeOffset? startedAt,
            DateTimeOffset? finishedAt)
        {
            Id = id;
            BackendId = backendId;
            Request = request;
            State = state;
            Result = result;
            Error = error;
            GroupId = groupId;
            Label = label;
            DependsOn = dependsOn;
            Tags = tags;
            CreatedAt = createdAt;
            StartedAt = startedAt;
            FinishedAt = finishedAt;
        }

        /// <summary>Unique job id.</summary>
        public string Id { get; }

        /// <summary>Backend the job runs on.</summary>
        public string BackendId { get; }

        /// <summary>The request as submitted (without automatically attached references).</summary>
        public GenerationRequest Request { get; }

        /// <summary>State at the time of the snapshot.</summary>
        public JobState State { get; }

        /// <summary>Result, once <see cref="State"/> is <see cref="JobState.Succeeded"/>.</summary>
        public GenerationResult? Result { get; }

        /// <summary>Failure, once <see cref="State"/> is <see cref="JobState.Failed"/>.</summary>
        public RenderException? Error { get; }

        /// <summary>Group the job belongs to, if any.</summary>
        public string? GroupId { get; }

        /// <summary>Human-readable label, if any.</summary>
        public string? Label { get; }

        /// <summary>Ids of the jobs this one waits for.</summary>
        public IReadOnlyList<string> DependsOn { get; }

        /// <summary>Caller metadata.</summary>
        public IReadOnlyDictionary<string, string> Tags { get; }

        /// <summary>When the job was enqueued.</summary>
        public DateTimeOffset CreatedAt { get; }

        /// <summary>When the job started running.</summary>
        public DateTimeOffset? StartedAt { get; }

        /// <summary>When the job reached a final state.</summary>
        public DateTimeOffset? FinishedAt { get; }

        /// <summary>True once the job can no longer change.</summary>
        public bool IsFinished =>
            State == JobState.Succeeded || State == JobState.Failed || State == JobState.Cancelled;
    }
}
