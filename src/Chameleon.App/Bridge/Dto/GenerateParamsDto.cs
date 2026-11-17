using System.Collections.Generic;

namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "jobs.submit" and of each item of "jobs.submitBatch".</summary>
    public sealed class GenerateParamsDto
    {
        /// <summary>Backend to run on.</summary>
        public string BackendId { get; set; } = string.Empty;

        /// <summary>Model to use.</summary>
        public string ModelId { get; set; } = string.Empty;

        /// <summary>Prompt text.</summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>Extra prompt for this item only, appended after a blank line.</summary>
        public string? AddonPrompt { get; set; }

        /// <summary>System prompt; null uses the user's setting or the catalog default.</summary>
        public string? SystemPrompt { get; set; }

        /// <summary>Asset id of the source image.</summary>
        public string? SourceAssetId { get; set; }

        /// <summary>Asset id of the mask.</summary>
        public string? MaskAssetId { get; set; }

        /// <summary>Reference images.</summary>
        public List<ReferenceParamsDto> References { get; set; } = new List<ReferenceParamsDto>();

        /// <summary>Aspect ratio; empty keeps the source ratio.</summary>
        public string? AspectRatio { get; set; }

        /// <summary>Size tier.</summary>
        public string? ImageSize { get; set; }

        /// <summary>Group for collective cancellation.</summary>
        public string? GroupId { get; set; }

        /// <summary>Ids of jobs whose results are attached as consistency references.</summary>
        public List<string> DependsOn { get; set; } = new List<string>();

        /// <summary>Human-readable label.</summary>
        public string? Label { get; set; }

        /// <summary>History node the result is attached to (phase 3).</summary>
        public string? ParentNodeId { get; set; }

        /// <summary>True when the UI adds the history node itself after compositing (phase 3).</summary>
        public bool DeferNode { get; set; }
    }
}
