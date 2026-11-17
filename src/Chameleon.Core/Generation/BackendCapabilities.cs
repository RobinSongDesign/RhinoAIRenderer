using System;
using System.Collections.Generic;
using System.Linq;

namespace Chameleon.Core.Generation
{
    /// <summary>What a backend can do for one model. Drives UI options and request validation.</summary>
    public sealed class BackendCapabilities
    {
        /// <summary>True when a mask can be sent alongside the source image.</summary>
        public bool SupportsMask { get; set; }

        /// <summary>Maximum number of input images (source plus all references). 0 means unknown/unlimited.</summary>
        public int MaxInputImages { get; set; }

        /// <summary>Accepted image size tiers, e.g. "1K". Empty means any.</summary>
        public IReadOnlyList<string> ImageSizes { get; set; } = Array.Empty<string>();

        /// <summary>Accepted aspect ratios, e.g. "16:9". Empty means any.</summary>
        public IReadOnlyList<string> AspectRatios { get; set; } = Array.Empty<string>();

        /// <summary>How many requests may run at the same time against this backend.</summary>
        public int MaxConcurrency { get; set; } = 1;

        /// <summary>Returns an independent copy.</summary>
        public BackendCapabilities Clone()
        {
            return new BackendCapabilities
            {
                SupportsMask = SupportsMask,
                MaxInputImages = MaxInputImages,
                ImageSizes = ImageSizes.ToArray(),
                AspectRatios = AspectRatios.ToArray(),
                MaxConcurrency = MaxConcurrency
            };
        }
    }
}
