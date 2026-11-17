using Chameleon.Core.Imaging;

namespace Chameleon.App.Assets
{
    /// <summary>Content-addressed storage for images exchanged between the host and the UI.</summary>
    public interface IAssetStore
    {
        /// <summary>Stores an image (a no-op when the same content is already stored) and describes it.</summary>
        AssetRef Put(ImageData image);

        /// <summary>Loads an image, or returns null when the id is unknown or malformed.</summary>
        ImageData? TryGet(string assetId);
    }
}
