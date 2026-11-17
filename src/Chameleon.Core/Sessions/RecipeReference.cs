using Chameleon.Core.Generation;

namespace Chameleon.Core.Sessions
{
    /// <summary>A reference image a generation used.</summary>
    public sealed class RecipeReference
    {
        /// <summary>Asset id (SHA-256) of the reference image.</summary>
        public string AssetId { get; set; } = string.Empty;

        /// <summary>What the image was for.</summary>
        public ReferenceRole Role { get; set; }

        /// <summary>The user's note on the image, if any.</summary>
        public string? Note { get; set; }

        /// <summary>Copies this reference.</summary>
        public RecipeReference Clone()
        {
            return new RecipeReference { AssetId = AssetId, Role = Role, Note = Note };
        }
    }
}
