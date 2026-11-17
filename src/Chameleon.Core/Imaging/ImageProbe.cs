using System;

namespace Chameleon.Core.Imaging
{
    /// <summary>
    /// Reads the format and pixel size of PNG, JPEG and WebP images from their headers.
    /// Pixels are never decoded here: all pixel work happens in the front end.
    /// </summary>
    public static class ImageProbe
    {
        /// <summary>MIME type reported for PNG images.</summary>
        public const string PngMimeType = "image/png";

        /// <summary>MIME type reported for JPEG images.</summary>
        public const string JpegMimeType = "image/jpeg";

        /// <summary>MIME type reported for WebP images.</summary>
        public const string WebpMimeType = "image/webp";

        /// <summary>Tries to identify an encoded image.</summary>
        /// <param name="bytes">Encoded image bytes; may be null or truncated.</param>
        /// <param name="mimeType">Detected MIME type, or an empty string on failure.</param>
        /// <param name="width">Width in pixels, or 0 on failure.</param>
        /// <param name="height">Height in pixels, or 0 on failure.</param>
        /// <returns>True when the format is supported and the size could be read.</returns>
        public static bool TryProbe(byte[]? bytes, out string mimeType, out int width, out int height)
        {
            mimeType = string.Empty;
            width = 0;
            height = 0;
            if (bytes == null || bytes.Length < 12)
            {
                return false;
            }

            if (TryProbePng(bytes, out width, out height))
            {
                mimeType = PngMimeType;
            }
            else if (TryProbeJpeg(bytes, out width, out height))
            {
                mimeType = JpegMimeType;
            }
            else if (TryProbeWebp(bytes, out width, out height))
            {
                mimeType = WebpMimeType;
            }

            if (mimeType.Length == 0 || width <= 0 || height <= 0)
            {
                mimeType = string.Empty;
                width = 0;
                height = 0;
                return false;
            }

            return true;
        }

        private static bool TryProbePng(byte[] b, out int width, out int height)
        {
            width = 0;
            height = 0;

            // 8-byte signature, then the IHDR chunk: length(4) "IHDR"(4) width(4) height(4).
            if (b.Length < 24 ||
                b[0] != 0x89 || b[1] != 0x50 || b[2] != 0x4E || b[3] != 0x47 ||
                b[4] != 0x0D || b[5] != 0x0A || b[6] != 0x1A || b[7] != 0x0A ||
                b[12] != (byte)'I' || b[13] != (byte)'H' || b[14] != (byte)'D' || b[15] != (byte)'R')
            {
                return false;
            }

            width = ReadInt32BigEndian(b, 16);
            height = ReadInt32BigEndian(b, 20);
            return true;
        }

        private static bool TryProbeJpeg(byte[] b, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (b[0] != 0xFF || b[1] != 0xD8)
            {
                return false;
            }

            int pos = 2;
            while (pos + 4 <= b.Length)
            {
                if (b[pos] != 0xFF)
                {
                    return false;
                }

                // Any number of 0xFF fill bytes may precede a marker.
                while (pos + 1 < b.Length && b[pos + 1] == 0xFF)
                {
                    pos++;
                }

                if (pos + 4 > b.Length)
                {
                    return false;
                }

                byte marker = b[pos + 1];

                // Standalone markers carry no length field.
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    pos += 2;
                    continue;
                }

                // Start of scan or end of image before any frame header: no size available.
                if (marker == 0xDA || marker == 0xD9)
                {
                    return false;
                }

                int segmentLength = (b[pos + 2] << 8) | b[pos + 3];
                if (segmentLength < 2)
                {
                    return false;
                }

                // SOF0..SOF15 hold the frame size, except DHT (C4), JPG (C8) and DAC (CC).
                bool isFrameHeader = marker >= 0xC0 && marker <= 0xCF &&
                                     marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (isFrameHeader)
                {
                    if (pos + 9 > b.Length)
                    {
                        return false;
                    }

                    height = (b[pos + 5] << 8) | b[pos + 6];
                    width = (b[pos + 7] << 8) | b[pos + 8];
                    return true;
                }

                pos += 2 + segmentLength;
            }

            return false;
        }

        private static bool TryProbeWebp(byte[] b, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (b.Length < 30 ||
                b[0] != (byte)'R' || b[1] != (byte)'I' || b[2] != (byte)'F' || b[3] != (byte)'F' ||
                b[8] != (byte)'W' || b[9] != (byte)'E' || b[10] != (byte)'B' || b[11] != (byte)'P' ||
                b[12] != (byte)'V' || b[13] != (byte)'P' || b[14] != (byte)'8')
            {
                return false;
            }

            switch (b[15])
            {
                case (byte)' ':
                    // Lossy: 3-byte frame tag, start code 9D 01 2A, then 14-bit width and height.
                    if (b[23] != 0x9D || b[24] != 0x01 || b[25] != 0x2A)
                    {
                        return false;
                    }

                    width = (b[26] | (b[27] << 8)) & 0x3FFF;
                    height = (b[28] | (b[29] << 8)) & 0x3FFF;
                    return true;

                case (byte)'L':
                    // Lossless: signature byte 0x2F, then width-1 and height-1 packed in 14 bits each.
                    if (b[20] != 0x2F)
                    {
                        return false;
                    }

                    uint bits = (uint)(b[21] | (b[22] << 8) | (b[23] << 16) | (b[24] << 24));
                    width = (int)(bits & 0x3FFF) + 1;
                    height = (int)((bits >> 14) & 0x3FFF) + 1;
                    return true;

                case (byte)'X':
                    // Extended: flags(1) reserved(3), then 24-bit canvas width-1 and height-1.
                    width = (b[24] | (b[25] << 8) | (b[26] << 16)) + 1;
                    height = (b[27] | (b[28] << 8) | (b[29] << 16)) + 1;
                    return true;

                default:
                    return false;
            }
        }

        private static int ReadInt32BigEndian(byte[] b, int offset)
        {
            return (b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3];
        }
    }
}
