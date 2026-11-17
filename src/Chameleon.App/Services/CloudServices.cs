using System;
using System.IO;
using System.Net.Http;
using Chameleon.Core.Backends;
using Chameleon.Core.Catalog;
using Chameleon.Core.Cloud;
using Chameleon.Core.Settings;

#pragma warning disable CS1591 // Properties are the bundle's parts, named after their types.

namespace Chameleon.App.Services
{
    /// <summary>The account-mode objects that belong together for one gateway address.</summary>
    public sealed class CloudServices
    {
        private CloudServices(CloudApiClient api, AuthSession session, CloudCatalogCache catalog, AccountService account, DeviceLoginFlow login, CloudBackend backend)
        {
            Api = api;
            Session = session;
            Catalog = catalog;
            Account = account;
            Login = login;
            Backend = backend;
        }

        public CloudApiClient Api { get; }

        public AuthSession Session { get; }

        public CloudCatalogCache Catalog { get; }

        public AccountService Account { get; }

        public DeviceLoginFlow Login { get; }

        public CloudBackend Backend { get; }

        /// <summary>
        /// Builds account mode when a gateway address is configured: the settings value wins over
        /// the build's default. Returns null when there is no (acceptable) address. A changed
        /// address takes effect on the next start.
        /// </summary>
        /// <param name="configuredBaseUrl">Address from the user's settings, if any.</param>
        /// <param name="defaultBaseUrl">Address built into this version, if any.</param>
        /// <param name="http">Shared HTTP client.</param>
        /// <param name="secrets">Holds the refresh token.</param>
        /// <param name="cacheDirectory">Where the catalog copy is kept.</param>
        /// <param name="language">Current UI language, read whenever the backend's name is shown.</param>
        public static CloudServices? TryCreate(string? configuredBaseUrl, string? defaultBaseUrl, HttpClient http, ISecretStore secrets, string cacheDirectory, Func<string> language)
        {
            Uri? baseUrl = CloudEndpoint.TryParse(configuredBaseUrl ?? defaultBaseUrl);
            if (baseUrl == null)
            {
                return null;
            }

            var api = new CloudApiClient(http, baseUrl);
            var session = new AuthSession(api, secrets);
            var catalog = new CloudCatalogCache(api, session, Path.Combine(cacheDirectory, "cloud-catalog.json"));
            var account = new AccountService(api, session, catalog);
            var login = new DeviceLoginFlow(api, session);
            var backend = new CloudBackend(api, session, catalog, () => DisplayName(language()));
            return new CloudServices(api, session, catalog, account, login, backend);
        }

        private static string DisplayName(string language)
        {
            return Languages.Normalize(language) == Languages.Chinese ? "Chameleon 账号" : "Chameleon account";
        }
    }
}
#pragma warning restore CS1591
