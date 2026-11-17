using System;
using Chameleon.Core.Imaging;
using Xunit;

namespace Chameleon.Core.Tests.Imaging
{
    public class ImageDataTests
    {
        [Fact]
        public void FromBytes_ExposesProbedMetadata()
        {
            byte[] bytes = TestFixtures.ReadBytes("images/jpeg_baseline_80x60.jpg");

            ImageData image = ImageData.FromBytes(bytes);

            Assert.Same(bytes, image.Bytes);
            Assert.Equal("image/jpeg", image.MimeType);
            Assert.Equal(80, image.Width);
            Assert.Equal(60, image.Height);
            Assert.Equal("jpg", image.FileExtension);
        }

        [Theory]
        [InlineData("images/png_64x48.png", "png")]
        [InlineData("images/webp_lossy_50x70.webp", "webp")]
        public void FileExtension_MatchesFormat(string fixture, string expected)
        {
            Assert.Equal(expected, ImageData.FromBytes(TestFixtures.ReadBytes(fixture)).FileExtension);
        }

        [Fact]
        public void Sha256Hex_IsStableAndContentAddressed()
        {
            ImageData a = ImageData.FromBytes(TestFixtures.ReadBytes("images/png_64x48.png"));
            ImageData b = ImageData.FromBytes(TestFixtures.ReadBytes("images/png_64x48.png"));
            ImageData c = ImageData.FromBytes(TestFixtures.ReadBytes("images/jpeg_baseline_80x60.jpg"));

            Assert.Equal(64, a.Sha256Hex.Length);
            Assert.Equal(a.Sha256Hex, a.Sha256Hex.ToLowerInvariant());
            Assert.Equal(a.Sha256Hex, b.Sha256Hex);
            Assert.NotEqual(a.Sha256Hex, c.Sha256Hex);
        }

        [Fact]
        public void FromBytes_ThrowsOnUnsupportedData()
        {
            Assert.Throws<FormatException>(() => ImageData.FromBytes(new byte[64]));
            Assert.Throws<ArgumentNullException>(() => ImageData.FromBytes(null!));
        }

        [Fact]
        public void TryFromBytes_ReturnsNullOnUnsupportedData()
        {
            Assert.Null(ImageData.TryFromBytes(new byte[64]));
            Assert.Null(ImageData.TryFromBytes(null));
            Assert.NotNull(ImageData.TryFromBytes(TestFixtures.ReadBytes("images/png_64x48.png")));
        }
    }
}
