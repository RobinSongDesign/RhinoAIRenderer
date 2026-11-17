using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Chameleon.Core.Imaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chameleon.Core.Providers.Http
{
    /// <summary>
    /// JSON request body that embeds images as base64 without ever materializing the whole body
    /// in memory. Callers build a small JSON skeleton and mark where each image goes; the images
    /// are base64-encoded straight into the network stream. The exact length is known up front,
    /// so the request is sent with Content-Length rather than chunked encoding.
    /// </summary>
    public sealed class StreamedImageJsonContent : HttpContent
    {
        // Multiple of 3 so every chunk but the last encodes without padding.
        private const int ChunkSize = 3 * 16 * 1024;

        private readonly List<byte[]> _segments = new List<byte[]>();
        private readonly List<ImageData> _images = new List<ImageData>();

        private StreamedImageJsonContent()
        {
            Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }

        /// <summary>Creates the content from a skeleton produced with a <see cref="Builder"/>.</summary>
        public static StreamedImageJsonContent Create(Builder builder, JToken skeleton)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var content = new StreamedImageJsonContent();
            string json = skeleton.ToString(Formatting.None);

            int cursor = 0;
            for (int i = 0; i < builder.Images.Count; i++)
            {
                // The placeholder is serialized as a JSON string; the quotes are kept around the base64.
                string token = builder.TokenFor(i);
                int at = json.IndexOf(token, cursor, StringComparison.Ordinal);
                if (at < 0)
                {
                    throw new InvalidOperationException("An image placeholder is missing from the JSON skeleton.");
                }

                content._segments.Add(Encoding.UTF8.GetBytes(json.Substring(cursor, at - cursor)));
                content._images.Add(builder.Images[i]);
                cursor = at + token.Length;
            }

            content._segments.Add(Encoding.UTF8.GetBytes(json.Substring(cursor)));
            return content;
        }

        /// <inheritdoc />
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var encoded = new char[ChunkSize / 3 * 4];
            var ascii = new byte[encoded.Length];

            for (int i = 0; i < _segments.Count; i++)
            {
                byte[] segment = _segments[i];
                await stream.WriteAsync(segment, 0, segment.Length).ConfigureAwait(false);

                if (i >= _images.Count)
                {
                    continue;
                }

                byte[] bytes = _images[i].Bytes;
                for (int offset = 0; offset < bytes.Length; offset += ChunkSize)
                {
                    int count = Math.Min(ChunkSize, bytes.Length - offset);
                    int chars = Convert.ToBase64CharArray(bytes, offset, count, encoded, 0);
                    for (int c = 0; c < chars; c++)
                    {
                        ascii[c] = (byte)encoded[c];
                    }

                    await stream.WriteAsync(ascii, 0, chars).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            foreach (byte[] segment in _segments)
            {
                length += segment.Length;
            }

            foreach (ImageData image in _images)
            {
                length += Base64Length(image.Bytes.Length);
            }

            return true;
        }

        private static long Base64Length(long byteCount)
        {
            return (byteCount + 2) / 3 * 4;
        }

        /// <summary>Hands out placeholders to put into the JSON skeleton where image data belongs.</summary>
        public sealed class Builder
        {
            // Random per request so prompt text can never collide with a placeholder.
            private readonly string _nonce = Guid.NewGuid().ToString("N");

            internal List<ImageData> Images { get; } = new List<ImageData>();

            /// <summary>Registers an image and returns the placeholder string to use as its JSON value.</summary>
            public string Add(ImageData image)
            {
                Images.Add(image ?? throw new ArgumentNullException(nameof(image)));
                return TokenFor(Images.Count - 1);
            }

            internal string TokenFor(int index)
            {
                return "@@chameleon-image:" + _nonce + ":" + index + "@@";
            }
        }
    }
}
