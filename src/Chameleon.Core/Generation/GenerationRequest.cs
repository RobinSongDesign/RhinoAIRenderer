using System;
using System.Collections.Generic;
using System.Linq;
using Chameleon.Core.Imaging;

namespace Chameleon.Core.Generation
{
    /// <summary>A provider-neutral description of one image generation or edit.</summary>
    public sealed class GenerationRequest
    {
        /// <summary>Default image size tier.</summary>
        public const string DefaultImageSize = "1K";

        /// <summary>Model identifier understood by the target backend.</summary>
        public string ModelId { get; set; } = string.Empty;

        /// <summary>What the user asked for.</summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>Standing instructions prepended to the prompt; null or empty for none.</summary>
        public string? SystemPrompt { get; set; }

        /// <summary>Viewport capture or the image being edited.</summary>
        public ImageData? Source { get; set; }

        /// <summary>Edit region: white pixels may change. Only valid when the backend supports masks.</summary>
        public ImageData? Mask { get; set; }

        /// <summary>Additional input images, in the order they should be sent.</summary>
        public IList<ReferenceImage> References { get; } = new List<ReferenceImage>();

        /// <summary>Target aspect ratio such as "16:9"; null or empty keeps the source ratio.</summary>
        public string? AspectRatio { get; set; }

        /// <summary>Size tier: "0.5K", "1K", "2K" or "4K".</summary>
        public string ImageSize { get; set; } = DefaultImageSize;

        /// <summary>Adapter-specific switches that have no neutral equivalent.</summary>
        public IDictionary<string, string> Extras { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Number of images sent to the model: the source plus every reference.</summary>
        public int InputImageCount => (Source != null ? 1 : 0) + References.Count;

        /// <summary>Returns a copy that shares the (immutable) images but has independent collections.</summary>
        public GenerationRequest Clone()
        {
            var copy = new GenerationRequest
            {
                ModelId = ModelId,
                Prompt = Prompt,
                SystemPrompt = SystemPrompt,
                Source = Source,
                Mask = Mask,
                AspectRatio = AspectRatio,
                ImageSize = ImageSize
            };

            foreach (ReferenceImage reference in References)
            {
                copy.References.Add(reference);
            }

            foreach (KeyValuePair<string, string> extra in Extras)
            {
                copy.Extras[extra.Key] = extra.Value;
            }

            return copy;
        }

        /// <summary>Checks the request against what the backend supports.</summary>
        /// <exception cref="RenderException">
        /// <see cref="RenderErrorCode.InvalidRequest"/> naming the first rule that is violated.
        /// </exception>
        public void Validate(BackendCapabilities capabilities)
        {
            if (capabilities == null)
            {
                throw new ArgumentNullException(nameof(capabilities));
            }

            if (string.IsNullOrWhiteSpace(ModelId))
            {
                throw Invalid("A model must be selected.");
            }

            if (string.IsNullOrWhiteSpace(Prompt))
            {
                throw Invalid("The prompt is empty.");
            }

            if (capabilities.ImageSizes.Count > 0 && !capabilities.ImageSizes.Contains(ImageSize))
            {
                throw Invalid("Image size '" + ImageSize + "' is not supported by this model.");
            }

            if (!string.IsNullOrEmpty(AspectRatio) &&
                capabilities.AspectRatios.Count > 0 &&
                !capabilities.AspectRatios.Contains(AspectRatio!))
            {
                throw Invalid("Aspect ratio '" + AspectRatio + "' is not supported by this model.");
            }

            if (capabilities.MaxInputImages > 0 && InputImageCount > capabilities.MaxInputImages)
            {
                throw Invalid(
                    "Too many input images: " + InputImageCount + " sent, the model accepts " +
                    capabilities.MaxInputImages + ".");
            }

            if (Mask != null && !capabilities.SupportsMask)
            {
                throw Invalid("This model does not support masks.");
            }

            if (Mask != null && Source == null)
            {
                throw Invalid("A mask requires a source image.");
            }
        }

        private static RenderException Invalid(string message)
        {
            return new RenderException(RenderErrorCode.InvalidRequest, message);
        }
    }
}
