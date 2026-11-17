using System.Collections.Generic;

namespace Chameleon.App.Bridge.Dto
{
    /// <summary>The current project's history.</summary>
    public sealed class SessionDto
    {
        /// <summary>Project id.</summary>
        public string ProjectId { get; set; } = string.Empty;

        /// <summary>Selected node, if any.</summary>
        public string? CurrentNodeId { get; set; }

        /// <summary>All nodes in the order they were added.</summary>
        public IReadOnlyList<NodeDto> Nodes { get; set; } = new List<NodeDto>();
    }
}
