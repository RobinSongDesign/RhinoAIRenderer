using Chameleon.Core.Imaging;

namespace Chameleon.Core.Tests
{
    /// <summary>Ready-made images for tests that only need "some valid image".</summary>
    internal static class TestImages
    {
        public static ImageData Png() => ImageData.FromBytes(TestFixtures.ReadBytes("images/png_64x48.png"));

        public static ImageData Jpeg() => ImageData.FromBytes(TestFixtures.ReadBytes("images/jpeg_baseline_80x60.jpg"));

        public static ImageData Webp() => ImageData.FromBytes(TestFixtures.ReadBytes("images/webp_lossy_50x70.webp"));

        public static ImageData ProgressiveJpeg() => ImageData.FromBytes(TestFixtures.ReadBytes("images/jpeg_progressive_72x54.jpg"));
    }
}
