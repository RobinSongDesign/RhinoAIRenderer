using System;
using Chameleon.Core.Generation;

namespace Chameleon.Core.Jobs
{
    /// <summary>One view of a batch render.</summary>
    public sealed class BatchItem
    {
        /// <summary>Creates a batch item.</summary>
        /// <param name="request">Request for this view, carrying the shared prompt.</param>
        /// <param name="addonPrompt">Extra prompt text for this view only.</param>
        /// <param name="label">Human-readable label, usually the view name.</param>
        public BatchItem(GenerationRequest request, string? addonPrompt = null, string? label = null)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            AddonPrompt = addonPrompt;
            Label = label;
        }

        /// <summary>Request for this view, carrying the shared prompt.</summary>
        public GenerationRequest Request { get; }

        /// <summary>Extra prompt text for this view only.</summary>
        public string? AddonPrompt { get; }

        /// <summary>Human-readable label.</summary>
        public string? Label { get; }
    }
}
