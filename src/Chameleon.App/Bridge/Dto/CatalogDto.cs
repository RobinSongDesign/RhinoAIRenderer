using System.Collections.Generic;
using Chameleon.Core.Catalog;

namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Catalog data the UI needs to build its controls.</summary>
    public sealed class CatalogDto
    {
        /// <summary>Prompt presets.</summary>
        public IReadOnlyList<StyleTemplate> StyleTemplates { get; set; } = new List<StyleTemplate>();

        /// <summary>System prompt used when the user has not set one.</summary>
        public string DefaultSystemPrompt { get; set; } = string.Empty;

        /// <summary>All image size tiers.</summary>
        public IReadOnlyList<string> ImageSizes { get; set; } = new List<string>();

        /// <summary>All aspect ratios.</summary>
        public IReadOnlyList<string> AspectRatios { get; set; } = new List<string>();

        /// <summary>Size tier selected by default.</summary>
        public string DefaultImageSize { get; set; } = string.Empty;

        /// <summary>Builds the DTO from a catalog.</summary>
        public static CatalogDto From(ChameleonCatalog catalog)
        {
            return new CatalogDto
            {
                StyleTemplates = catalog.StyleTemplates,
                DefaultSystemPrompt = catalog.DefaultSystemPrompt,
                ImageSizes = catalog.ImageSizes,
                AspectRatios = catalog.AspectRatios,
                DefaultImageSize = catalog.DefaultImageSize
            };
        }
    }
}
