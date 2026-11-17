using System;
using System.Collections.Generic;
using System.Net.Http;
using Chameleon.App.Hosting;
using Chameleon.App.Server;
using Chameleon.Core.Backends;
using Chameleon.Core.Generation;
using Chameleon.Core.Settings;

namespace Chameleon.App
{
    /// <summary>What a host must supply to start the application.</summary>
    public sealed class ChameleonAppOptions
    {
        /// <summary>Creates the options with the required collaborators.</summary>
        public ChameleonAppOptions(IRhinoHost host, IAppPaths paths, IStaticFileProvider staticFiles)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Paths = paths ?? throw new ArgumentNullException(nameof(paths));
            StaticFiles = staticFiles ?? throw new ArgumentNullException(nameof(staticFiles));
        }

        /// <summary>Rhino and desktop services.</summary>
        public IRhinoHost Host { get; }

        /// <summary>File locations.</summary>
        public IAppPaths Paths { get; }

        /// <summary>Front-end files.</summary>
        public IStaticFileProvider StaticFiles { get; }

        /// <summary>Secret store; null picks the platform's default.</summary>
        public ISecretStore? SecretStore { get; set; }

        /// <summary>Process environment; null uses the real one.</summary>
        public IEnvironmentInfo? Environment { get; set; }

        /// <summary>HTTP handler for provider calls; null uses the default. Tests inject a stub.</summary>
        public HttpMessageHandler? HttpMessageHandler { get; set; }

        /// <summary>Retry behavior for provider calls; null uses the default.</summary>
        public RetryPolicy? RetryPolicy { get; set; }

        /// <summary>
        /// Gateway address for account mode when the user's settings do not set one; defaults to
        /// the build's address (none yet). Null with no setting means account mode is not offered.
        /// </summary>
        public string? CloudBaseUrl { get; set; } = Core.Cloud.CloudEndpoint.DefaultBaseUrl;

        /// <summary>Backends offered in addition to the configured providers.</summary>
        public IList<IRenderBackend> ExtraBackends { get; } = new List<IRenderBackend>();

        /// <summary>Fixed server port; development host only.</summary>
        public int? Port { get; set; }

        /// <summary>Fixed server token; development host only.</summary>
        public string? Token { get; set; }

        /// <summary>Extra allowed browser origin; development host only.</summary>
        public string? AllowedDevOrigin { get; set; }
    }
}
