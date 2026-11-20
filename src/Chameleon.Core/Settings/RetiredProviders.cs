using System;
using System.Collections.Generic;
using System.Linq;
using Chameleon.Core.Backends;
using Chameleon.Core.Providers;

namespace Chameleon.Core.Settings
{
    /// <summary>
    /// Providers whose service shut down (BLT AI, 柏拉图AI, in 2026-11). The WPF version shipped it
    /// as a built-in and users often added copies of it themselves; both kinds are removed, keys
    /// included, so the provider list does not offer a dead endpoint.
    /// </summary>
    public static class RetiredProviders
    {
        /// <summary>Ids of retired built-in providers of the WPF version.</summary>
        public static readonly IReadOnlyCollection<string> BuiltInIds = new[] { "BltAI" };

        // Domains of the retired service; subdomains count too (hk-api.gptbest.vip, api.bltcy.ai).
        private static readonly string[] Domains = { "gptbest.vip", "bltcy.ai" };

        // The service's Chinese name, which users put in the names of their own copies.
        private const string ServiceName = "柏拉图";

        /// <summary>True when a provider the user added is a copy of a retired service.</summary>
        public static bool IsRetired(UserProviderConfig provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            return (provider.DisplayName ?? string.Empty).Contains(ServiceName) || IsRetiredEndpoint(provider.BaseUrl);
        }

        /// <summary>True when the address belongs to a retired service.</summary>
        public static bool IsRetiredEndpoint(string? baseUrl)
        {
            if (!Uri.TryCreate((baseUrl ?? string.Empty).Trim(), UriKind.Absolute, out Uri? uri))
            {
                return false;
            }

            string host = uri.Host.ToLowerInvariant();
            return Domains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
        }

        /// <summary>True when <see cref="Prune"/> would change anything.</summary>
        public static bool HasAny(AppSettings settings, ISecretStore secrets)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (secrets == null)
            {
                throw new ArgumentNullException(nameof(secrets));
            }

            return settings.UserProviders.Any(IsRetired) ||
                   BuiltInIds.Any(id => settings.BuiltInOverrides.ContainsKey(id) || secrets.Contains(SecretNames.ForProvider(id)));
        }

        /// <summary>
        /// Removes retired providers from the settings and their keys from the secret store.
        /// A selection that pointed at one falls back to <paramref name="fallbackBackendId"/>.
        /// </summary>
        /// <returns>Ids of the providers removed; empty when the settings were already clean.</returns>
        public static IReadOnlyList<string> Prune(AppSettings settings, ISecretStore secrets, string fallbackBackendId)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (secrets == null)
            {
                throw new ArgumentNullException(nameof(secrets));
            }

            List<string> removed = settings.UserProviders.Where(IsRetired).Select(p => p.Id).ToList();
            settings.UserProviders.RemoveAll(IsRetired);
            foreach (string id in BuiltInIds)
            {
                bool hadOverride = settings.BuiltInOverrides.Remove(id);
                if (hadOverride || secrets.Contains(SecretNames.ForProvider(id)))
                {
                    removed.Add(id);
                }
            }

            foreach (string id in removed.Distinct())
            {
                string backendId = BackendIds.Direct(id);
                secrets.Delete(SecretNames.ForProvider(id));
                settings.ActiveModelByBackend.Remove(backendId);
                settings.SignedInAccounts.Remove(id);
                settings.DiscoveredModels.Remove(id);
                if (settings.ActiveBackendId == backendId)
                {
                    settings.ActiveBackendId = fallbackBackendId;
                }
            }

            return removed.Distinct().ToList();
        }
    }
}
