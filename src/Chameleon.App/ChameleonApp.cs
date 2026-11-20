using System;
using System.IO;
using System.Net.Http;
using Chameleon.App.Assets;
using Chameleon.App.Bridge;
using Chameleon.App.Hosting;
using Chameleon.App.Platform;
using Chameleon.App.Projects;
using Chameleon.App.Server;
using Chameleon.App.Services;
using Chameleon.Core.Backends;
using Chameleon.Core.Catalog;
using Chameleon.Core.Jobs;
using Chameleon.Core.Prompts;
using Chameleon.Core.Providers;
using Chameleon.Core.Providers.Accounts;
using Chameleon.Core.Providers.Balance;
using Chameleon.Core.Settings;

namespace Chameleon.App
{
    /// <summary>
    /// The running application: every service wired together behind the local bridge server.
    /// This is the single composition root, shared by the Rhino plug-in and the development host.
    /// </summary>
    public sealed class ChameleonApp : IDisposable
    {
        private readonly LocalServer _server;
        private readonly GenerationQueue _queue;
        private readonly HttpClient _httpClient;

        private ChameleonApp(
            LocalServer server,
            GenerationQueue queue,
            HttpClient httpClient,
            ProjectService projects,
            RpcRouter router,
            EventBus events,
            IAssetStore assets,
            ISettingsStore settings,
            ISecretStore secrets)
        {
            _server = server;
            _queue = queue;
            _httpClient = httpClient;
            Projects = projects;
            Router = router;
            Events = events;
            Assets = assets;
            Settings = settings;
            Secrets = secrets;
        }

        /// <summary>Address the WebView must load; contains the secret token.</summary>
        public string BaseUrl => _server.BaseUrl;

        /// <summary>Edit histories; the Rhino host binds documents to projects through it.</summary>
        public ProjectService Projects { get; }

        /// <summary>RPC entry point; hosts may register additional methods before the UI connects.</summary>
        public RpcRouter Router { get; }

        /// <summary>Event channel to the UI.</summary>
        public EventBus Events { get; }

        /// <summary>Image storage shared with the UI.</summary>
        public IAssetStore Assets { get; }

        /// <summary>User settings.</summary>
        public ISettingsStore Settings { get; }

        /// <summary>Secret storage.</summary>
        public ISecretStore Secrets { get; }

        /// <summary>Builds every service and starts the bridge server.</summary>
        public static ChameleonApp Start(ChameleonAppOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            IRhinoHost host = options.Host;
            IAppPaths paths = options.Paths;
            IEnvironmentInfo environment = options.Environment ?? new SystemEnvironmentInfo();

            ChameleonCatalog catalog = CatalogLoader.LoadBuiltIn();
            var settingsStore = new JsonSettingsStore(paths.SettingsFile, () => AppSettingsDefaults.Create(catalog));
            ISecretStore secrets = options.SecretStore ?? SecretStoreFactory.Create(host.Info.Platform, paths, environment);

            if (paths.LegacySettingsFile != null)
            {
                LegacyMigrationResult migration = LegacySettingsMigrator.TryMigrate(paths.LegacySettingsFile, settingsStore, secrets);
                if (migration.Migrated)
                {
                    host.Log("Imported settings from the previous version (" + migration.KeysMigrated + " API key(s)).");
                }
            }

            HttpClient httpClient = DirectBackendFactory.CreateSharedHttpClient(options.HttpMessageHandler);
            var settings = new SettingsEditor(settingsStore);
            var factory = new DirectBackendFactory(AdapterRegistry.CreateDefault(), httpClient, new PromptComposer(), options.RetryPolicy);
            CloudServices? cloud = CloudServices.TryCreate(
                settingsStore.Load().Cloud.BaseUrl,
                options.CloudBaseUrl,
                httpClient,
                secrets,
                paths.CacheDirectory,
                () => settings.Read().Language);
            var extraBackends = new System.Collections.Generic.List<Core.Generation.IRenderBackend>(options.ExtraBackends);
            if (cloud != null)
            {
                extraBackends.Insert(0, cloud.Backend);
            }

            var backends = new BackendRegistry(catalog, settings, secrets, factory, extraBackends);
            var queue = new GenerationQueue(backends.Find);

            var cache = new DiskAssetStore(Path.Combine(paths.CacheDirectory, "assets"));
            cache.PurgeStale(DiskAssetStore.DefaultMaxAge);
            var projects = new ProjectService(new DiskProjectStore(paths.ProjectsDirectory, host.Log), cache);
            projects.PurgeAbandonedTemporaryProjects(TimeSpan.FromDays(1));
            IAssetStore assets = new ProjectAssetStore(cache, projects);

            var events = new EventBus(host.Log);
            var router = new RpcRouter(host.Log);

            var backendService = new BackendService(backends, settings, events);
            var accountService = new AccountRpcService(host, cloud, events, backendService, queue);
            new AppStateService(host, paths, catalog, settings, backends, events, accountService, cloud?.Catalog).Register(router);
            backendService.Register(router);
            accountService.Register(router);
            var discovery = new ModelDiscoveryService(catalog, backends, settings, secrets, backendService, log: host.Log);
            discovery.Register(router);
            var balanceClient = new OneApiBalanceClient(httpClient);
            var providers = new ProviderService(catalog, settings, secrets, backends, backendService, factory, discovery, balanceClient);
            providers.Register(router);
            new ProviderSignInService(backends, providers, new NewApiAccountClient(httpClient), ProviderSignInService.KeyNameFor(environment.MachineName)).Register(router);
            // Pick up models the providers added or retired since the last run, without delaying start-up.
            _ = discovery.RefreshStaleAsync(ModelDiscoveryService.DefaultMaxAge, System.Threading.CancellationToken.None);
            new BalanceService(backends, secrets, balanceClient).Register(router);
            new CaptureService(host, assets).Register(router);
            new JobService(queue, backends, assets, settings, catalog, events, projects, host.Log).Register(router);
            new SessionService(projects, assets, events).Register(router);

            if (cloud != null && cloud.Session.IsSignedIn)
            {
                // Refresh models, prices and the balance in the background; the cached copies serve meanwhile.
                _ = RefreshAccountAsync(cloud, host);
            }
            new FileService(host, assets).Register(router);

            var server = new LocalServer(new LocalServerOptions(options.StaticFiles, router, events, assets)
            {
                Port = options.Port,
                Token = options.Token,
                AllowedDevOrigin = options.AllowedDevOrigin,
                Log = host.Log
            });
            server.Start();

            return new ChameleonApp(server, queue, httpClient, projects, router, events, assets, settingsStore, secrets);
        }

        private static async System.Threading.Tasks.Task RefreshAccountAsync(CloudServices cloud, IRhinoHost host)
        {
            try
            {
                await cloud.Catalog.RefreshAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);
                await cloud.Account.GetMeAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);
            }
            catch (Core.Generation.RenderException ex)
            {
                host.Log("Refreshing the account failed: " + ex.Message);
            }
        }

        /// <summary>Stops the server, cancels running jobs, saves the histories and releases network resources.</summary>
        public void Dispose()
        {
            _server.Dispose();
            _queue.Dispose();
            // After the queue, so nothing adds nodes while pending saves are written.
            Projects.Dispose();
            _httpClient.Dispose();
        }
    }
}
