using System;
using System.Collections.Generic;
using Chameleon.Core.Catalog;
using Chameleon.Core.Providers;

namespace Chameleon.Core.Settings
{
    /// <summary>
    /// User settings as stored in settings.json (schema version 2). Secrets are never part of
    /// this document; they live in an <see cref="ISecretStore"/>.
    /// </summary>
    public sealed class AppSettings
    {
        /// <summary>Schema version written by this build.</summary>
        public const int CurrentSchemaVersion = 2;

        /// <summary>Schema version of the document.</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>UI language: see <see cref="Languages"/>.</summary>
        public string Language { get; set; } = Languages.English;

        /// <summary>Backend used for new generations: "direct:{providerId}" or "cloud".</summary>
        public string ActiveBackendId { get; set; } = string.Empty;

        /// <summary>Last model picked for each backend, keyed by backend id.</summary>
        public Dictionary<string, string> ActiveModelByBackend { get; set; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Providers the user added.</summary>
        public List<UserProviderConfig> UserProviders { get; set; } = new List<UserProviderConfig>();

        /// <summary>User changes to built-in providers, keyed by provider id.</summary>
        public Dictionary<string, ProviderOverride> BuiltInOverrides { get; set; } =
            new Dictionary<string, ProviderOverride>(StringComparer.Ordinal);

        /// <summary>User-defined system prompt; null uses the catalog default.</summary>
        public string? SystemPrompt { get; set; }

        /// <summary>Batch rendering options.</summary>
        public BatchSettings Batch { get; set; } = new BatchSettings();

        /// <summary>Account mode options.</summary>
        public CloudSettings Cloud { get; set; } = new CloudSettings();

        /// <summary>
        /// Models built-in providers reported for the stored key, keyed by provider id. They replace
        /// the catalog's list (an explicit override still wins), so models the endpoint added or
        /// retired since this build show up without a new release.
        /// </summary>
        public Dictionary<string, DiscoveredModels> DiscoveredModels { get; set; } =
            new Dictionary<string, DiscoveredModels>(StringComparer.Ordinal);

        /// <summary>
        /// Account names of providers the user signed in to from the plug-in, keyed by provider id.
        /// Only present while the stored key came from that sign-in; a pasted key removes the entry.
        /// </summary>
        public Dictionary<string, string> SignedInAccounts { get; set; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
