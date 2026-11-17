using System;
using System.Collections.Generic;
using Chameleon.Core.Imaging;

namespace Chameleon.Core.Generation
{
    /// <summary>What a backend produced for one request.</summary>
    public sealed class GenerationResult
    {
        /// <summary>Creates a result.</summary>
        /// <param name="images">Generated images; at least one.</param>
        /// <param name="text">Text returned by the model alongside the images, if any.</param>
        /// <param name="usage">Billing information; null for direct (bring-your-own-key) backends.</param>
        /// <param name="requestId">Identifier of the request, for correlating logs.</param>
        public GenerationResult(IReadOnlyList<ImageData> images, string? text, UsageInfo? usage, string requestId)
        {
            Images = images ?? throw new ArgumentNullException(nameof(images));
            Text = text;
            Usage = usage;
            RequestId = requestId ?? string.Empty;
        }

        /// <summary>Generated images.</summary>
        public IReadOnlyList<ImageData> Images { get; }

        /// <summary>Text part returned by the model, if any.</summary>
        public string? Text { get; }

        /// <summary>Billing information, when the backend reports it.</summary>
        public UsageInfo? Usage { get; }

        /// <summary>Identifier of the request.</summary>
        public string RequestId { get; }
    }
}
