using System;
using Chameleon.App.Assets;
using Chameleon.Core.Sessions;

namespace Chameleon.App.Bridge.Dto
{
    /// <summary>A history node as the UI sees it.</summary>
    public sealed class NodeDto
    {
        /// <summary>Node id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Parent node, or null for a root.</summary>
        public string? ParentId { get; set; }

        /// <summary>How the image entered the history.</summary>
        public NodeKind Kind { get; set; }

        /// <summary>The node's image.</summary>
        public AssetRef Asset { get; set; } = new AssetRef();

        /// <summary>When the node was added.</summary>
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>User-visible label.</summary>
        public string? Label { get; set; }

        /// <summary>Marked as a favourite.</summary>
        public bool Starred { get; set; }

        /// <summary>How the image was produced; null for captures and imports.</summary>
        public Recipe? Recipe { get; set; }

        /// <summary>Camera of a capture.</summary>
        public CameraState? Camera { get; set; }
    }
}
