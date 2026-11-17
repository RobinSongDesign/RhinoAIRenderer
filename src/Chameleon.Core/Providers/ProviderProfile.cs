using System.Collections.Generic;
using System.Linq;
using Chameleon.Core.Generation;

namespace Chameleon.Core.Providers
{
    /// <summary>Everything needed to reach one endpoint with a user-supplied API key.</summary>
    public sealed class ProviderProfile
    {
        /// <summary>Catalog id for built-ins, a generated id for user-defined providers.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Name shown to the user.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Endpoint root without a trailing slash.</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>Wire format: see <see cref="ApiFormats"/>.</summary>
        public string ApiFormat { get; set; } = ApiFormats.Gemini;

        /// <summary>How the key is sent: see <see cref="AuthTypes"/>.</summary>
        public string AuthType { get; set; } = AuthTypes.Bearer;

        /// <summary>Models offered by this endpoint.</summary>
        public IList<ModelInfo> Models { get; set; } = new List<ModelInfo>();

        /// <summary>Model selected by default.</summary>
        public string DefaultModel { get; set; } = string.Empty;

        /// <summary>Page where the user can obtain a key; null for user-defined providers.</summary>
        public string? ApiKeyUrl { get; set; }

        /// <summary>True for providers that come from the catalog.</summary>
        public bool IsBuiltIn { get; set; }

        /// <summary>True when the user changed a built-in provider's settings.</summary>
        public bool IsOverridden { get; set; }

        /// <summary>Whether the Gemini request enables Google Search grounding. On by default, as before.</summary>
        public bool EnableSearchGrounding { get; set; } = true;

        /// <summary>Finds a model by id; null when this provider does not list it.</summary>
        public ModelInfo? FindModel(string modelId)
        {
            return Models.FirstOrDefault(m => m.Id == modelId);
        }
    }
}
