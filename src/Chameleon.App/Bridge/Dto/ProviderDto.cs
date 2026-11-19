using System.Collections.Generic;

namespace Chameleon.App.Bridge.Dto
{
    /// <summary>A provider as edited in the settings dialog. Never carries the API key.</summary>
    public sealed class ProviderDto
    {
        /// <summary>Provider id; empty when creating a new provider.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Name shown to the user.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Endpoint root.</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>"gemini" or "openai".</summary>
        public string ApiFormat { get; set; } = string.Empty;

        /// <summary>"goog" or "bearer".</summary>
        public string AuthType { get; set; } = string.Empty;

        /// <summary>Model ids.</summary>
        public List<string> Models { get; set; } = new List<string>();

        /// <summary>Model selected by default.</summary>
        public string DefaultModel { get; set; } = string.Empty;

        /// <summary>Page where a key can be obtained; built-ins only.</summary>
        public string? ApiKeyUrl { get; set; }

        /// <summary>True for catalog providers.</summary>
        public bool IsBuiltIn { get; set; }

        /// <summary>True when a built-in provider has user changes.</summary>
        public bool IsOverridden { get; set; }

        /// <summary>Whether Gemini requests enable Google Search grounding.</summary>
        public bool EnableSearchGrounding { get; set; } = true;

        /// <summary>True when an API key is stored for this provider.</summary>
        public bool HasKey { get; set; }

        /// <summary>How the balance is queried: "oneapi" (One API / new-api relays) or null for not at all.</summary>
        public string? BalanceApi { get; set; }

        /// <summary>Page where the user tops up or redeems credit (https only).</summary>
        public string? TopUpUrl { get; set; }

        /// <summary>True when the user can sign in to the provider's account instead of pasting a key.</summary>
        public bool SupportsSignIn { get; set; }

        /// <summary>Account name when the stored key came from signing in.</summary>
        public string? SignedInAs { get; set; }
    }
}
