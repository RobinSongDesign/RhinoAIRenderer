using System;
using System.Security.Cryptography;
using System.Text;

namespace Chameleon.Core.Imaging
{
    /// <summary>
    /// An encoded image held in memory. Instances are immutable and the pixels are never decoded
    /// on the .NET side; only the header is read to learn the format and size.
    /// </summary>
    public sealed class ImageData
    {
        private readonly Lazy<string> _sha256Hex;

        private ImageData(byte[] bytes, string mimeType, int width, int height)
        {
            Bytes = bytes;
            MimeType = mimeType;
            Width = width;
            Height = height;
            _sha256Hex = new Lazy<string>(ComputeSha256Hex);
        }

        /// <summary>Encoded bytes. Treat as read-only: the content hash is cached.</summary>
        public byte[] Bytes { get; }

        /// <summary>"image/png", "image/jpeg" or "image/webp".</summary>
        public string MimeType { get; }

        /// <summary>Width in pixels.</summary>
        public int Width { get; }

        /// <summary>Height in pixels.</summary>
        public int Height { get; }

        /// <summary>Lower-case hexadecimal SHA-256 of <see cref="Bytes"/>; used as the asset id.</summary>
        public string Sha256Hex => _sha256Hex.Value;

        /// <summary>File extension without the dot: "png", "jpg" or "webp".</summary>
        public string FileExtension
        {
            get
            {
                switch (MimeType)
                {
                    case ImageProbe.JpegMimeType: return "jpg";
                    case ImageProbe.WebpMimeType: return "webp";
                    default: return "png";
                }
            }
        }

        /// <summary>Wraps encoded bytes after probing their header.</summary>
        /// <param name="bytes">PNG, JPEG or WebP data. The array is not copied.</param>
        /// <exception cref="ArgumentNullException"><paramref name="bytes"/> is null.</exception>
        /// <exception cref="FormatException">The data is not a supported image.</exception>
        public static ImageData FromBytes(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            if (!ImageProbe.TryProbe(bytes, out string mimeType, out int width, out int height))
            {
                throw new FormatException("The data is not a supported image (PNG, JPEG or WebP).");
            }

            return new ImageData(bytes, mimeType, width, height);
        }

        /// <summary>Returns the image when the bytes are a supported image, otherwise null.</summary>
        public static ImageData? TryFromBytes(byte[]? bytes)
        {
            if (bytes != null && ImageProbe.TryProbe(bytes, out string mimeType, out int width, out int height))
            {
                return new ImageData(bytes, mimeType, width, height);
            }

            return null;
        }

        private string ComputeSha256Hex()
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }
    }
}
