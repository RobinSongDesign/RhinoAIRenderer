using System;
using System.IO;
using System.IO.Compression;

namespace ChameleonSpike
{
    /// <summary>
    /// Minimal PNG encoder used to produce a large test image without System.Drawing, so the
    /// transport checks do not depend on the capture checks.
    /// </summary>
    public static class PngWriter
    {
        private static readonly uint[] CrcTable = BuildCrcTable();

        /// <summary>Encodes an RGB gradient of the given size.</summary>
        public static byte[] Gradient(int width, int height)
        {
            // Each scanline: filter byte 0 followed by RGB triples.
            int stride = (width * 3) + 1;
            var raw = new byte[stride * height];
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                byte g = (byte)(y * 255 / Math.Max(1, height - 1));
                for (int x = 0; x < width; x++)
                {
                    int p = row + 1 + (x * 3);
                    raw[p] = (byte)(x * 255 / Math.Max(1, width - 1));
                    raw[p + 1] = g;
                    raw[p + 2] = 96;
                }
            }

            using (var output = new MemoryStream())
            {
                output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

                var header = new byte[13];
                WriteBigEndian(header, 0, (uint)width);
                WriteBigEndian(header, 4, (uint)height);
                header[8] = 8;  // bit depth
                header[9] = 2;  // color type: RGB
                WriteChunk(output, "IHDR", header);

                WriteChunk(output, "IDAT", ZlibCompress(raw));
                WriteChunk(output, "IEND", new byte[0]);
                return output.ToArray();
            }
        }

        private static byte[] ZlibCompress(byte[] data)
        {
            using (var buffer = new MemoryStream())
            {
                buffer.WriteByte(0x78);
                buffer.WriteByte(0x01);
                using (var deflate = new DeflateStream(buffer, CompressionLevel.Fastest, true))
                {
                    deflate.Write(data, 0, data.Length);
                }

                uint a = 1, b = 0;
                foreach (byte value in data)
                {
                    a = (a + value) % 65521;
                    b = (b + a) % 65521;
                }

                var adler = new byte[4];
                WriteBigEndian(adler, 0, (b << 16) | a);
                buffer.Write(adler, 0, 4);
                return buffer.ToArray();
            }
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, (uint)data.Length);
            output.Write(length, 0, 4);

            var typeBytes = new[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
            output.Write(typeBytes, 0, 4);
            output.Write(data, 0, data.Length);

            uint crc = UpdateCrc(UpdateCrc(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
            var crcBytes = new byte[4];
            WriteBigEndian(crcBytes, 0, crc);
            output.Write(crcBytes, 0, 4);
        }

        private static uint UpdateCrc(uint crc, byte[] data)
        {
            foreach (byte value in data)
            {
                crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
            }

            return crc;
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }

        private static void WriteBigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
