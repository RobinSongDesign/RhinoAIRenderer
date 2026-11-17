namespace Chameleon.App.Assets
{
    /// <summary>Describes a stored image to the UI.</summary>
    public sealed class AssetRef
    {
        /// <summary>SHA-256 of the image bytes, lower-case hexadecimal.</summary>
        public string AssetId { get; set; } = string.Empty;

        /// <summary>Image MIME type.</summary>
        public string MimeType { get; set; } = string.Empty;

        /// <summary>Width in pixels.</summary>
        public int Width { get; set; }

        /// <summary>Height in pixels.</summary>
        public int Height { get; set; }

        /// <summary>Encoded size in bytes.</summary>
        public long Bytes { get; set; }
    }
}
