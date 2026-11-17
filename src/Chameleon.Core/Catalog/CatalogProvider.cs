using System.Collections.Generic;

namespace Chameleon.Core.Catalog
{
    /// <summary>A built-in provider shipped with the catalog.</summary>
    public sealed class CatalogProvider
    {
        /// <summary>Stable identifier; also the key for overrides and stored API keys.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Name shown to the user.</summary>
        public LocalizedText DisplayName { get; set; } = new LocalizedText();

        /// <summary>Endpoint root without a trailing slash.</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>Wire format: see <see cref="Providers.ApiFormats"/>.</summary>
        public string ApiFormat { get; set; } = string.Empty;

        /// <summary>How the key is sent: see <see cref="Providers.AuthTypes"/>.</summary>
        public string AuthType { get; set; } = string.Empty;

        /// <summary>Model selected by default.</summary>
        public string DefaultModel { get; set; } = string.Empty;

        /// <summary>Page where the user can obtain an API key.</summary>
        public string? ApiKeyUrl { get; set; }

        /// <summary>Models offered by this provider.</summary>
        public List<CatalogModel> Models { get; set; } = new List<CatalogModel>();
    }
}
