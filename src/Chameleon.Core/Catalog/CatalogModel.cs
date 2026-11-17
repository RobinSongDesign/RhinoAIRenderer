using Chameleon.Core.Generation;

namespace Chameleon.Core.Catalog
{
    /// <summary>A model entry in the catalog.</summary>
    public sealed class CatalogModel
    {
        /// <summary>Identifier sent to the provider.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Friendly name, e.g. "Nano Banana Pro"; empty to show the id.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Capabilities of this model; null to use the defaults of the provider's API format.</summary>
        public BackendCapabilities? Capabilities { get; set; }
    }
}
