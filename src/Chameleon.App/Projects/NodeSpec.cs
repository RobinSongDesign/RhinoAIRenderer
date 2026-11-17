using Chameleon.Core.Sessions;

namespace Chameleon.App.Projects
{
    /// <summary>What a new history node consists of; the project assigns its id and time.</summary>
    public sealed class NodeSpec
    {
        /// <summary>Parent node, or null for a new root.</summary>
        public string? ParentId { get; set; }

        /// <summary>How the image entered the history.</summary>
        public NodeKind Kind { get; set; }

        /// <summary>The node's image.</summary>
        public string AssetId { get; set; } = string.Empty;

        /// <summary>User-visible label.</summary>
        public string? Label { get; set; }

        /// <summary>How the image was produced.</summary>
        public Recipe? Recipe { get; set; }

        /// <summary>Camera of a capture.</summary>
        public CameraState? Camera { get; set; }
    }
}
