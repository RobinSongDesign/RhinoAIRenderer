using System;
using System.Collections.Concurrent;
using Chameleon.Core.Imaging;

namespace Chameleon.App.Assets
{
    /// <summary>Asset store that keeps everything in memory. For tests and short-lived tools.</summary>
    public sealed class InMemoryAssetStore : IAssetStore
    {
        private readonly ConcurrentDictionary<string, ImageData> _images =
            new ConcurrentDictionary<string, ImageData>(StringComparer.Ordinal);

        /// <summary>Number of distinct images stored.</summary>
        public int Count => _images.Count;

        /// <inheritdoc />
        public AssetRef Put(ImageData image)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            _images.TryAdd(image.Sha256Hex, image);
            return AssetIds.Describe(image);
        }

        /// <inheritdoc />
        public ImageData? TryGet(string assetId)
        {
            return AssetIds.IsValid(assetId) && _images.TryGetValue(assetId, out ImageData? image) ? image : null;
        }
    }
}
