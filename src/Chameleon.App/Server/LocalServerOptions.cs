using System;
using Chameleon.App.Assets;
using Chameleon.App.Bridge;

namespace Chameleon.App.Server
{
    /// <summary>Everything a <see cref="LocalServer"/> needs.</summary>
    public sealed class LocalServerOptions
    {
        /// <summary>Creates the options with the required collaborators.</summary>
        public LocalServerOptions(IStaticFileProvider staticFiles, RpcRouter router, EventBus events, IAssetStore assets)
        {
            StaticFiles = staticFiles ?? throw new ArgumentNullException(nameof(staticFiles));
            Router = router ?? throw new ArgumentNullException(nameof(router));
            Events = events ?? throw new ArgumentNullException(nameof(events));
            Assets = assets ?? throw new ArgumentNullException(nameof(assets));
        }

        /// <summary>Front-end files.</summary>
        public IStaticFileProvider StaticFiles { get; }

        /// <summary>Handles RPC calls.</summary>
        public RpcRouter Router { get; }

        /// <summary>Source of server-sent events.</summary>
        public EventBus Events { get; }

        /// <summary>Image storage behind the asset endpoints.</summary>
        public IAssetStore Assets { get; }

        /// <summary>Fixed port; null picks a free one. Only the development host fixes the port.</summary>
        public int? Port { get; set; }

        /// <summary>Fixed path token; null generates a random one. Only the development host fixes the token.</summary>
        public string? Token { get; set; }

        /// <summary>
        /// Extra origin allowed to call the server cross-origin (the Vite dev server). Must stay
        /// null in the plug-in.
        /// </summary>
        public string? AllowedDevOrigin { get; set; }

        /// <summary>Receives diagnostic lines; never receives the token.</summary>
        public Action<string>? Log { get; set; }
    }
}
