using System.Collections.Generic;

namespace Chameleon.Core.Providers
{
    /// <summary>
    /// User changes to a built-in provider. Only non-empty fields replace the catalog value, so a
    /// catalog update still reaches the fields the user never touched.
    /// </summary>
    public sealed class ProviderOverride
    {
        /// <summary>Replacement display name.</summary>
        public string? DisplayName { get; set; }

        /// <summary>Replacement endpoint root.</summary>
        public string? BaseUrl { get; set; }

        /// <summary>Replacement wire format.</summary>
        public string? ApiFormat { get; set; }

        /// <summary>Replacement authentication style.</summary>
        public string? AuthType { get; set; }

        /// <summary>Replacement model list; null or empty keeps the catalog list.</summary>
        public List<string>? Models { get; set; }

        /// <summary>Replacement default model.</summary>
        public string? DefaultModel { get; set; }

        /// <summary>Replacement search-grounding switch.</summary>
        public bool? EnableSearchGrounding { get; set; }
    }
}
