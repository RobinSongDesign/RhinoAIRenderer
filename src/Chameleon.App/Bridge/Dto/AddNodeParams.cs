using Chameleon.Core.Sessions;

namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "session.addNode".</summary>
    public sealed class AddNodeParams
    {
        /// <summary>Parent node, or null for a new root.</summary>
        public string? ParentId { get; set; }

        /// <summary>How the image entered the history.</summary>
        public NodeKind Kind { get; set; }

        /// <summary>The node's image (already uploaded or captured).</summary>
        public string AssetId { get; set; } = string.Empty;

        /// <summary>User-visible label.</summary>
        public string? Label { get; set; }

        /// <summary>How the image was produced (edits composited by the UI).</summary>
        public Recipe? Recipe { get; set; }

        /// <summary>Camera of a capture.</summary>
        public CameraState? Camera { get; set; }
    }
}
