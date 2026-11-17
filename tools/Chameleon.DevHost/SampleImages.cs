using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chameleon.Core.Imaging;

namespace Chameleon.DevHost
{
    /// <summary>The sample images shipped next to the development host executable.</summary>
    public sealed class SampleImages
    {
        private readonly string _directory;

        /// <summary>Creates the accessor for the samples folder next to the executable.</summary>
        public SampleImages()
        {
            _directory = Path.Combine(AppContext.BaseDirectory, "samples");
        }

        /// <summary>Names of the fake document's named views.</summary>
        public IReadOnlyList<string> NamedViews { get; } = new[] { "Exterior", "Interior", "Aerial" };

        /// <summary>Path of the image the fake "open file" dialog returns.</summary>
        public string ReferencePath => Path.Combine(_directory, "reference-style.jpg");

        /// <summary>Capture of the active view, or of a named view.</summary>
        public ImageData Capture(string viewName = "active")
        {
            return Load(Path.Combine(_directory, "capture-" + viewName + ".png"));
        }

        /// <summary>Paths of the fake generation results, in a stable order.</summary>
        public IReadOnlyList<string> ResultPaths()
        {
            return Directory.GetFiles(Path.Combine(_directory, "results")).OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        /// <summary>Loads an image file.</summary>
        public static ImageData Load(string path)
        {
            return ImageData.FromBytes(File.ReadAllBytes(path));
        }
    }
}
