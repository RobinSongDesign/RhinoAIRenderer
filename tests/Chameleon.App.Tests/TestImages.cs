using System;
using System.IO;
using Chameleon.Core.Imaging;

namespace Chameleon.App.Tests
{
    /// <summary>Ready-made images for tests that only need "some valid image".</summary>
    internal static class TestImages
    {
        public static ImageData Png() => Load("png_64x48.png");

        public static ImageData Jpeg() => Load("jpeg_baseline_80x60.jpg");

        public static ImageData Webp() => Load("webp_lossy_50x70.webp");

        public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "images", name);

        private static ImageData Load(string name) => ImageData.FromBytes(File.ReadAllBytes(PathOf(name)));
    }
}
