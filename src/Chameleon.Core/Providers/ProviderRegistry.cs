using System;
using System.Collections.Generic;
using System.Linq;
using Chameleon.Core.Catalog;
using Chameleon.Core.Generation;
using Chameleon.Core.Settings;

namespace Chameleon.Core.Providers
{
    /// <summary>
    /// Produces the effective provider list: catalog built-ins with the user's overrides applied,
    /// followed by the providers the user added.
    /// </summary>
    public sealed class ProviderRegistry
    {
        private readonly ChameleonCatalog _catalog;

        /// <summary>Creates a registry over a catalog.</summary>
        public ProviderRegistry(ChameleonCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>Builds the effective provider list.</summary>
        /// <param name="userProviders">Providers added by the user; may be null.</param>
        /// <param name="overrides">Overrides keyed by built-in provider id; may be null.</param>
        /// <param name="language">Language used for built-in display names.</param>
        /// <param name="discovered">Models built-in providers reported, keyed by provider id; may be null.</param>
        public IReadOnlyList<ProviderProfile> Build(
            IEnumerable<UserProviderConfig>? userProviders,
            IDictionary<string, ProviderOverride>? overrides,
            string? language,
            IDictionary<string, DiscoveredModels>? discovered = null)
        {
            var result = new List<ProviderProfile>();

            foreach (CatalogProvider builtIn in _catalog.Providers)
            {
                ProviderOverride? providerOverride = null;
                overrides?.TryGetValue(builtIn.Id, out providerOverride);
                DiscoveredModels? found = null;
                discovered?.TryGetValue(builtIn.Id, out found);
                result.Add(FromBuiltIn(builtIn, providerOverride, language, found));
            }

            if (userProviders != null)
            {
                var usedIds = new HashSet<string>(result.Select(p => p.Id), StringComparer.Ordinal);
                foreach (UserProviderConfig config in userProviders)
                {
                    // A user entry can never shadow a built-in or another user entry.
                    if (!string.IsNullOrWhiteSpace(config.Id) && usedIds.Add(config.Id))
                    {
                        result.Add(FromUser(config));
                    }
                }
            }

            return result;
        }

        private ProviderProfile FromBuiltIn(CatalogProvider builtIn, ProviderOverride? ov, string? language, DiscoveredModels? discovered)
        {
            string apiFormat = NonEmpty(ov?.ApiFormat) ?? builtIn.ApiFormat;
            bool hasModelOverride = ov?.Models != null && ov.Models.Count > 0;
            bool hasDiscovered = discovered?.Models != null && discovered.Models.Count > 0;
            // The user's own list wins, then what the endpoint reported, then the catalog.
            IEnumerable<string> modelIds = hasModelOverride
                ? ov!.Models!
                : hasDiscovered ? discovered!.Models : builtIn.Models.Select(m => m.Id);

            List<ModelInfo> models = ResolveModels(apiFormat, modelIds, builtIn.Id);

            return new ProviderProfile
            {
                Id = builtIn.Id,
                DisplayName = NonEmpty(ov?.DisplayName) ?? builtIn.DisplayName.Get(language),
                BaseUrl = TrimUrl(NonEmpty(ov?.BaseUrl) ?? builtIn.BaseUrl),
                ApiFormat = apiFormat,
                AuthType = NonEmpty(ov?.AuthType) ?? builtIn.AuthType,
                Models = models,
                DefaultModel = PickDefaultModel(models, ov?.DefaultModel, builtIn.DefaultModel),
                // The key page always comes from the catalog: an override must not redirect it.
                ApiKeyUrl = builtIn.ApiKeyUrl,
                IsBuiltIn = true,
                IsOverridden = ov != null,
                EnableSearchGrounding = ov?.EnableSearchGrounding ?? true,
                // Like the key page, these describe the service itself and are not overridable.
                BalanceApi = KnownBalanceApi(builtIn.BalanceApi),
                TopUpUrl = HttpsOrNull(builtIn.TopUpUrl),
                SignInApi = Accounts.SignInApis.IsKnown(builtIn.SignInApi) ? builtIn.SignInApi : null
            };
        }

        private ProviderProfile FromUser(UserProviderConfig config)
        {
            string apiFormat = NonEmpty(config.ApiFormat) ?? ApiFormats.Gemini;
            List<ModelInfo> models = ResolveModels(apiFormat, config.Models ?? new List<string>(), null);

            return new ProviderProfile
            {
                Id = config.Id,
                DisplayName = NonEmpty(config.DisplayName) ?? config.Id,
                BaseUrl = TrimUrl(config.BaseUrl ?? string.Empty),
                ApiFormat = apiFormat,
                AuthType = NonEmpty(config.AuthType) ?? AuthTypes.Bearer,
                Models = models,
                DefaultModel = PickDefaultModel(models, config.DefaultModel, null),
                ApiKeyUrl = null,
                IsBuiltIn = false,
                IsOverridden = false,
                EnableSearchGrounding = config.EnableSearchGrounding,
                BalanceApi = KnownBalanceApi(config.BalanceApi),
                TopUpUrl = HttpsOrNull(config.TopUpUrl)
            };
        }

        private static string? KnownBalanceApi(string? balanceApi)
        {
            return BalanceApis.IsKnown(balanceApi) ? balanceApi : null;
        }

        // Top-up pages are opened in the system browser, which only ever gets https addresses.
        private static string? HttpsOrNull(string? url)
        {
            return url != null && Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps
                ? uri.AbsoluteUri
                : null;
        }

        private List<ModelInfo> ResolveModels(string apiFormat, IEnumerable<string> modelIds, string? builtInProviderId)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var models = new List<ModelInfo>();
            foreach (string raw in modelIds)
            {
                string id = (raw ?? string.Empty).Trim();
                if (id.Length > 0 && seen.Add(id))
                {
                    models.Add(_catalog.ResolveModel(apiFormat, id, builtInProviderId));
                }
            }

            return models;
        }

        private static string PickDefaultModel(List<ModelInfo> models, string? preferred, string? fallback)
        {
            if (!string.IsNullOrEmpty(preferred) && models.Any(m => m.Id == preferred))
            {
                return preferred!;
            }

            if (!string.IsNullOrEmpty(fallback) && models.Any(m => m.Id == fallback))
            {
                return fallback!;
            }

            return models.Count > 0 ? models[0].Id : string.Empty;
        }

        private static string? NonEmpty(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }

        private static string TrimUrl(string url)
        {
            return url.Trim().TrimEnd('/');
        }
    }
}
