using System;
using Chameleon.App.Assets;
using Chameleon.Core.Imaging;

namespace Chameleon.App.Projects
{
    /// <summary>
    /// The asset view the bridge and the services use: new images go to the working cache, and
    /// lookups try the open projects first, since their images outlive the cache's purge.
    /// </summary>
    public sealed class ProjectAssetStore : IAssetStore
    {
        private readonly IAssetStore _cache;
        private readonly ProjectService _projects;

        /// <summary>Creates the view.</summary>
        public ProjectAssetStore(IAssetStore cache, ProjectService projects)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        }

        /// <inheritdoc />
        public AssetRef Put(ImageData image)
        {
            return _cache.Put(image);
        }

        /// <inheritdoc />
        public ImageData? TryGet(string assetId)
        {
            return _projects.TryGetAsset(assetId) ?? _cache.TryGet(assetId);
        }
    }
}
