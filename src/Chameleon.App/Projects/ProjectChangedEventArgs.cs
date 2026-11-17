using System;

namespace Chameleon.App.Projects
{
    /// <summary>Identifies the project whose history changed.</summary>
    public sealed class ProjectChangedEventArgs : EventArgs
    {
        /// <summary>Creates the arguments.</summary>
        public ProjectChangedEventArgs(string projectId)
        {
            ProjectId = projectId;
        }

        /// <summary>Project that changed.</summary>
        public string ProjectId { get; }
    }
}
