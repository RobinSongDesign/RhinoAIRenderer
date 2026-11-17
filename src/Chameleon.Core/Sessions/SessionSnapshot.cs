using System.Collections.Generic;

namespace Chameleon.Core.Sessions
{
    /// <summary>The persisted form of an <see cref="EditSession"/> (the project file).</summary>
    public sealed class SessionSnapshot
    {
        /// <summary>Version of this format, for future migrations.</summary>
        public int SchemaVersion { get; set; } = EditSession.CurrentSchemaVersion;

        /// <summary>Project id.</summary>
        public string ProjectId { get; set; } = string.Empty;

        /// <summary>Selected node, if any.</summary>
        public string? CurrentNodeId { get; set; }

        /// <summary>All nodes in the order they were added.</summary>
        public IList<ImageNode> Nodes { get; set; } = new List<ImageNode>();
    }
}
