using System.Collections.Generic;
using Chameleon.Core.Imaging;
using Chameleon.Core.Sessions;

namespace Chameleon.App.Projects
{
    /// <summary>What generation needs from the history: where results go and how to record them.</summary>
    public interface IProjectHistory
    {
        /// <summary>Project of the active document.</summary>
        string CurrentProjectId { get; }

        /// <summary>
        /// Adds a node to a project (not necessarily the current one), copying the images it needs
        /// into the project. <paramref name="images"/> supplies bytes for assets that may not be
        /// stored anywhere yet.
        /// </summary>
        ImageNode AddNode(string projectId, NodeSpec spec, IEnumerable<ImageData>? images = null);

        /// <summary>True when the node exists in the project.</summary>
        bool Contains(string projectId, string nodeId);
    }
}
