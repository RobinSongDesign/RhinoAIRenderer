using System;
using Chameleon.Core.Imaging;

namespace Chameleon.Core.Generation
{
    /// <summary>An extra input image and the role it plays in a generation.</summary>
    public sealed class ReferenceImage
    {
        /// <summary>Creates a reference.</summary>
        /// <param name="image">The reference image.</param>
        /// <param name="role">How the model should use it.</param>
        /// <param name="note">Optional user note, e.g. "use this wood for the floor".</param>
        public ReferenceImage(ImageData image, ReferenceRole role, string? note = null)
        {
            Image = image ?? throw new ArgumentNullException(nameof(image));
            Role = role;
            Note = note;
        }

        /// <summary>The reference image.</summary>
        public ImageData Image { get; }

        /// <summary>How the model should use the image.</summary>
        public ReferenceRole Role { get; }

        /// <summary>Optional user note describing the reference.</summary>
        public string? Note { get; }
    }
}
