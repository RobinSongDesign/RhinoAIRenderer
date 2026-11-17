using System.Collections.Generic;
using System.Linq;

namespace Chameleon.Core.Sessions
{
    /// <summary>
    /// How a node was produced: enough to show the parameters again and to run the same request.
    /// The source image is the parent node's image.
    /// </summary>
    public sealed class Recipe
    {
        /// <summary>Backend that produced the image.</summary>
        public string BackendId { get; set; } = string.Empty;

        /// <summary>Model that produced the image.</summary>
        public string ModelId { get; set; } = string.Empty;

        /// <summary>The user's prompt.</summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>System prompt in effect, or null when none was sent.</summary>
        public string? SystemPrompt { get; set; }

        /// <summary>Requested aspect ratio; null or empty kept the source ratio.</summary>
        public string? AspectRatio { get; set; }

        /// <summary>Requested size tier.</summary>
        public string ImageSize { get; set; } = string.Empty;

        /// <summary>Mask that limited the change, if any.</summary>
        public string? MaskAssetId { get; set; }

        /// <summary>Reference images in the order they were sent.</summary>
        public IList<RecipeReference> References { get; set; } = new List<RecipeReference>();

        /// <summary>Kind of edit ("inpaint", "quick:{id}"), or null for a plain generation.</summary>
        public string? EditKind { get; set; }

        /// <summary>Every asset the recipe refers to (mask and references).</summary>
        public IEnumerable<string> GetAssetIds()
        {
            if (!string.IsNullOrEmpty(MaskAssetId))
            {
                yield return MaskAssetId!;
            }

            foreach (RecipeReference reference in References)
            {
                yield return reference.AssetId;
            }
        }

        /// <summary>Deep copy, so callers cannot change a stored recipe.</summary>
        public Recipe Clone()
        {
            return new Recipe
            {
                BackendId = BackendId,
                ModelId = ModelId,
                Prompt = Prompt,
                SystemPrompt = SystemPrompt,
                AspectRatio = AspectRatio,
                ImageSize = ImageSize,
                MaskAssetId = MaskAssetId,
                References = References.Select(r => r.Clone()).ToList(),
                EditKind = EditKind
            };
        }
    }
}
