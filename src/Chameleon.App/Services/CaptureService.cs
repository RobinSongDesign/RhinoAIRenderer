using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Assets;
using Chameleon.App.Bridge;
using Chameleon.App.Bridge.Dto;
using Chameleon.App.Hosting;
using Chameleon.Core.Imaging;

namespace Chameleon.App.Services
{
    /// <summary>Viewport captures: asks the host for an image and hands the UI an asset reference.</summary>
    public sealed class CaptureService
    {
        private const int MaxCaptureDimension = 8192;

        private readonly IRhinoHost _host;
        private readonly IAssetStore _assets;

        /// <summary>Creates the service.</summary>
        public CaptureService(IRhinoHost host, IAssetStore assets)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        }

        /// <summary>Registers this service's RPC methods.</summary>
        public void Register(RpcRouter router)
        {
            router.RegisterRaw("rhino.captureActiveView", async (parameters, ct) =>
                await CaptureActiveViewAsync(ReadOptions(parameters == null ? null : BridgeJson.ToObject<CaptureParams>(parameters)), ct).ConfigureAwait(false));
            router.Register("rhino.listNamedViews", ListNamedViewsAsync);
            router.Register<CaptureParams, AssetRef>("rhino.captureNamedView", (p, ct) =>
                CaptureNamedViewAsync(p.Name ?? string.Empty, ReadOptions(p), ct));
        }

        /// <summary>Captures the active viewport.</summary>
        public async Task<AssetRef> CaptureActiveViewAsync(CaptureOptions options, CancellationToken cancellationToken)
        {
            ImageData image = await _host.CaptureActiveViewAsync(options, cancellationToken).ConfigureAwait(false);
            return _assets.Put(image);
        }

        /// <summary>Lists the document's named views.</summary>
        public async Task<IReadOnlyList<object>> ListNamedViewsAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<string> names = await _host.GetNamedViewsAsync(cancellationToken).ConfigureAwait(false);
            return names.Select(name => (object)new { name }).ToList();
        }

        /// <summary>Captures a named view.</summary>
        public async Task<AssetRef> CaptureNamedViewAsync(string name, CaptureOptions options, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw BridgeException.InvalidRequest("A named view is required.");
            }

            ImageData image = await _host.CaptureNamedViewAsync(name, options, cancellationToken).ConfigureAwait(false);
            return _assets.Put(image);
        }

        private static CaptureOptions ReadOptions(CaptureParams? parameters)
        {
            var options = new CaptureOptions { Width = parameters?.Width, Height = parameters?.Height };
            if ((options.Width.HasValue && (options.Width < 1 || options.Width > MaxCaptureDimension)) ||
                (options.Height.HasValue && (options.Height < 1 || options.Height > MaxCaptureDimension)))
            {
                throw BridgeException.InvalidRequest("Capture size must be between 1 and " + MaxCaptureDimension + " pixels.");
            }

            return options;
        }
    }
}
