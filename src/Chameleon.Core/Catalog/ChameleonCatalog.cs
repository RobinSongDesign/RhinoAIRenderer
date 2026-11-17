using System;
using System.Collections.Generic;
using System.Linq;
using Chameleon.Core.Generation;

namespace Chameleon.Core.Catalog
{
    /// <summary>
    /// Everything that is data rather than code: built-in providers and models, style templates,
    /// selectable sizes and the default system prompt. The cloud gateway serves the same shape.
    /// </summary>
    public sealed class ChameleonCatalog
    {
        /// <summary>Schema version of the catalog document.</summary>
        public int Version { get; set; } = 1;

        /// <summary>Provider selected on first launch.</summary>
        public string DefaultProviderId { get; set; } = string.Empty;

        /// <summary>System prompt used when the user has not set their own.</summary>
        public string DefaultSystemPrompt { get; set; } = string.Empty;

        /// <summary>Image size tier selected by default.</summary>
        public string DefaultImageSize { get; set; } = GenerationRequest.DefaultImageSize;

        /// <summary>All image size tiers the UI may offer.</summary>
        public List<string> ImageSizes { get; set; } = new List<string>();

        /// <summary>All aspect ratios the UI may offer (the "keep source ratio" choice is implicit).</summary>
        public List<string> AspectRatios { get; set; } = new List<string>();

        /// <summary>Capabilities assumed for a model the catalog does not know, keyed by API format.</summary>
        public Dictionary<string, BackendCapabilities> DefaultCapabilities { get; set; } =
            new Dictionary<string, BackendCapabilities>(StringComparer.Ordinal);

        /// <summary>Built-in providers.</summary>
        public List<CatalogProvider> Providers { get; set; } = new List<CatalogProvider>();

        /// <summary>Prompt presets.</summary>
        public List<StyleTemplate> StyleTemplates { get; set; } = new List<StyleTemplate>();

        /// <summary>Finds a built-in provider by id; null when unknown.</summary>
        public CatalogProvider? FindProvider(string id)
        {
            return Providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        }

        /// <summary>Returns a private copy of the default capabilities for an API format.</summary>
        public BackendCapabilities GetDefaultCapabilities(string apiFormat)
        {
            return DefaultCapabilities.TryGetValue(apiFormat ?? string.Empty, out BackendCapabilities? caps)
                ? caps.Clone()
                : new BackendCapabilities();
        }

        /// <summary>
        /// Describes a model for a provider. Known models keep their catalog name and capabilities
        /// even when offered by a user-defined endpoint of the same format; unknown models get the
        /// format's defaults and are shown by id.
        /// </summary>
        /// <param name="apiFormat">Wire format of the provider offering the model.</param>
        /// <param name="modelId">Model identifier.</param>
        /// <param name="preferredProviderId">Built-in provider to look in first, if any.</param>
        public ModelInfo ResolveModel(string apiFormat, string modelId, string? preferredProviderId = null)
        {
            CatalogModel? known = null;
            if (preferredProviderId != null)
            {
                known = FindProvider(preferredProviderId)?.Models.FirstOrDefault(m => m.Id == modelId);
            }

            if (known == null)
            {
                known = Providers
                    .Where(p => string.Equals(p.ApiFormat, apiFormat, StringComparison.Ordinal))
                    .SelectMany(p => p.Models)
                    .FirstOrDefault(m => m.Id == modelId);
            }

            return new ModelInfo
            {
                Id = modelId,
                DisplayName = string.IsNullOrEmpty(known?.DisplayName) ? modelId : known!.DisplayName,
                Capabilities = known?.Capabilities?.Clone() ?? GetDefaultCapabilities(apiFormat)
            };
        }
    }
}
