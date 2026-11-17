using System;
using System.Linq;
using Chameleon.Core.Imaging;
using Xunit;

namespace Chameleon.Core.Tests.Imaging
{
    public class ImageProbeTests
    {
        [Theory]
        [InlineData("images/png_64x48.png", "image/png", 64, 48)]
        [InlineData("images/jpeg_baseline_80x60.jpg", "image/jpeg", 80, 60)]
        [InlineData("images/jpeg_progressive_72x54.jpg", "image/jpeg", 72, 54)]
        [InlineData("images/jpeg_exif_90x40.jpg", "image/jpeg", 90, 40)]
        [InlineData("images/webp_lossy_50x70.webp", "image/webp", 50, 70)]
        [InlineData("images/webp_lossless_33x21.webp", "image/webp", 33, 21)]
        [InlineData("images/webp_extended_44x28.webp", "image/webp", 44, 28)]
        public void TryProbe_ReadsFormatAndSize(string fixture, string expectedMime, int expectedWidth, int expectedHeight)
        {
            byte[] bytes = TestFixtures.ReadBytes(fixture);

            bool ok = ImageProbe.TryProbe(bytes, out string mime, out int width, out int height);

            Assert.True(ok);
            Assert.Equal(expectedMime, mime);
            Assert.Equal(expectedWidth, width);
            Assert.Equal(expectedHeight, height);
        }

        [Fact]
        public void Fixtures_CoverAllThreeWebpContainers()
        {
            // Guards the fixture generator: each file must really exercise a different chunk type.
            Assert.Equal((byte)' ', TestFixtures.ReadBytes("images/webp_lossy_50x70.webp")[15]);
            Assert.Equal((byte)'L', TestFixtures.ReadBytes("images/webp_lossless_33x21.webp")[15]);
            Assert.Equal((byte)'X', TestFixtures.ReadBytes("images/webp_extended_44x28.webp")[15]);
        }

        [Theory]
        [InlineData("images/png_64x48.png", 20)]
        [InlineData("images/jpeg_baseline_80x60.jpg", 30)]
        [InlineData("images/jpeg_exif_90x40.jpg", 200)]
        [InlineData("images/webp_lossy_50x70.webp", 25)]
        public void TryProbe_RejectsTruncatedFiles(string fixture, int keepBytes)
        {
            byte[] truncated = TestFixtures.ReadBytes(fixture).Take(keepBytes).ToArray();

            Assert.False(ImageProbe.TryProbe(truncated, out string mime, out int width, out int height));
            Assert.Equal(string.Empty, mime);
            Assert.Equal(0, width);
            Assert.Equal(0, height);
        }

        [Fact]
        public void TryProbe_RejectsNullEmptyAndNonImageData()
        {
            Assert.False(ImageProbe.TryProbe(null, out _, out _, out _));
            Assert.False(ImageProbe.TryProbe(Array.Empty<byte>(), out _, out _, out _));
            Assert.False(ImageProbe.TryProbe(System.Text.Encoding.ASCII.GetBytes("this is definitely not an image file"), out _, out _, out _));
        }

        [Fact]
        public void TryProbe_SkipsJpegFillBytesBeforeMarkers()
        {
            // SOI, a padded APP0 marker (extra 0xFF fill bytes), then SOF0 for a 3x2 image.
            byte[] jpeg =
            {
                0xFF, 0xD8,
                0xFF, 0xFF, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
                0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x02, 0x00, 0x03, 0x01, 0x01, 0x11, 0x00
            };

            Assert.True(ImageProbe.TryProbe(jpeg, out string mime, out int width, out int height));
            Assert.Equal("image/jpeg", mime);
            Assert.Equal(3, width);
            Assert.Equal(2, height);
        }

        [Fact]
        public void TryProbe_DoesNotTreatHuffmanTableAsFrameHeader()
        {
            // DHT (C4) sits in the SOF marker range but carries no size; the scan starts before any SOF.
            byte[] jpeg =
            {
                0xFF, 0xD8,
                0xFF, 0xC4, 0x00, 0x06, 0x01, 0x02, 0x03, 0x04,
                0xFF, 0xDA, 0x00, 0x02, 0x00, 0x00
            };

            Assert.False(ImageProbe.TryProbe(jpeg, out _, out _, out _));
        }
    }
}
