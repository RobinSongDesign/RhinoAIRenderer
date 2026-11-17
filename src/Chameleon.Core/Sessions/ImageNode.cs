using System;
using System.Collections.Generic;

namespace Chameleon.Core.Sessions
{
    /// <summary>One image in a project's history tree.</summary>
    public sealed class ImageNode
    {
        /// <summary>Unique id within the project.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Parent node, or null for a root (a capture or an import, typically).</summary>
        public string? ParentId { get; set; }

        /// <summary>How the image entered the history.</summary>
        public NodeKind Kind { get; set; }

        /// <summary>Asset id (SHA-256) of the image.</summary>
        public string AssetId { get; set; } = string.Empty;

        /// <summary>When the node was added.</summary>
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>How the image was produced; null for captures and imports.</summary>
        public Recipe? Recipe { get; set; }

        /// <summary>User-visible label, e.g. a named view.</summary>
        public string? Label { get; set; }

        /// <summary>Marked as a favourite by the user.</summary>
        public bool Starred { get; set; }

        /// <summary>Camera of a capture node, used to align Rhino object masks.</summary>
        public CameraState? Camera { get; set; }

        /// <summary>Every asset this node needs: its image and whatever its recipe refers to.</summary>
        public IEnumerable<string> GetAssetIds()
        {
            yield return AssetId;
            if (Recipe != null)
            {
                foreach (string id in Recipe.GetAssetIds())
                {
                    yield return id;
                }
            }
        }

        /// <summary>Deep copy, so callers cannot change a stored node.</summary>
        public ImageNode Clone()
        {
            return new ImageNode
            {
                Id = Id,
                ParentId = ParentId,
                Kind = Kind,
                AssetId = AssetId,
                CreatedAt = CreatedAt,
                Recipe = Recipe?.Clone(),
                Label = Label,
                Starred = Starred,
                Camera = Camera?.Clone()
            };
        }
    }
}
