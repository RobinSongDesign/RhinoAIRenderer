using System;
using System.Collections.Generic;
using Chameleon.Core.Generation;

namespace Chameleon.Core.Jobs
{
    /// <summary>One entry of a batch plan: a request plus the plan entries it depends on.</summary>
    public sealed class PlannedJob
    {
        /// <summary>Creates a plan entry.</summary>
        /// <param name="request">Request to run.</param>
        /// <param name="dependsOnIndexes">Indexes of earlier entries in the same plan.</param>
        /// <param name="label">Human-readable label.</param>
        public PlannedJob(GenerationRequest request, IReadOnlyList<int> dependsOnIndexes, string? label)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            DependsOnIndexes = dependsOnIndexes ?? throw new ArgumentNullException(nameof(dependsOnIndexes));
            Label = label;
        }

        /// <summary>Request to run.</summary>
        public GenerationRequest Request { get; }

        /// <summary>Indexes of earlier plan entries whose results feed this one.</summary>
        public IReadOnlyList<int> DependsOnIndexes { get; }

        /// <summary>Human-readable label.</summary>
        public string? Label { get; }

        /// <summary>Caller metadata copied onto the job.</summary>
        public IDictionary<string, string> Tags { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
