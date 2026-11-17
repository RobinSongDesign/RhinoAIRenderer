using System.Collections.Generic;

namespace Chameleon.Core.Providers
{
    /// <summary>A provider the user added in settings, as stored in the settings file.</summary>
    public sealed class UserProviderConfig
    {
        /// <summary>Generated identifier, stable for the life of the entry.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Name chosen by the user.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Endpoint root.</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>Wire format: see <see cref="ApiFormats"/>.</summary>
        public string ApiFormat { get; set; } = ApiFormats.Gemini;

        /// <summary>How the key is sent: see <see cref="AuthTypes"/>.</summary>
        public string AuthType { get; set; } = AuthTypes.Bearer;

        /// <summary>Model identifiers the endpoint offers.</summary>
        public List<string> Models { get; set; } = new List<string>();

        /// <summary>Model selected by default; empty to use the first one.</summary>
        public string DefaultModel { get; set; } = string.Empty;

        /// <summary>Whether Gemini requests enable Google Search grounding.</summary>
        public bool EnableSearchGrounding { get; set; } = true;
    }
}
