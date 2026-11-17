using Chameleon.Core.Imaging;

namespace Chameleon.App.Assets
{
    /// <summary>Helpers for asset identifiers.</summary>
    public static class AssetIds
    {
        private const int Length = 64;

        /// <summary>
        /// True when the text is a well-formed asset id (64 lower-case hex digits). Ids end up in
        /// file names and URLs, so anything else is rejected before it is used.
        /// </summary>
        public static bool IsValid(string? assetId)
        {
            if (assetId == null || assetId.Length != Length)
            {
                return false;
            }

            foreach (char c in assetId)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Builds the description of an image.</summary>
        public static AssetRef Describe(ImageData image)
        {
            return new AssetRef
            {
                AssetId = image.Sha256Hex,
                MimeType = image.MimeType,
                Width = image.Width,
                Height = image.Height,
                Bytes = image.Bytes.LongLength
            };
        }
    }
}
