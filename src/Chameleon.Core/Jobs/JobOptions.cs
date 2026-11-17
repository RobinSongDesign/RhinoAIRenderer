using System;
using System.Collections.Generic;

namespace Chameleon.Core.Jobs
{
    /// <summary>Scheduling options and caller metadata for a job.</summary>
    public sealed class JobOptions
    {
        /// <summary>Default cap on automatically attached consistency references.</summary>
        public const int DefaultMaxConsistencyRefs = 3;

        /// <summary>Groups jobs of one batch or one multi-candidate request so they can be cancelled together.</summary>
        public string? GroupId { get; set; }

        /// <summary>Human-readable label, e.g. the named view being rendered.</summary>
        public string? Label { get; set; }

        /// <summary>
        /// Ids of jobs that must finish first. The first image of each one that succeeded is attached
        /// to this job's request as a consistency reference.
        /// </summary>
        public IList<string> DependsOn { get; } = new List<string>();

        /// <summary>Most consistency references attached from dependencies; the most recent ones win.</summary>
        public int MaxConsistencyRefs { get; set; } = DefaultMaxConsistencyRefs;

        /// <summary>Opaque caller data carried through to the job snapshot (e.g. history node ids).</summary>
        public IDictionary<string, string> Tags { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
