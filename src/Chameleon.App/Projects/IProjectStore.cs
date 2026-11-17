using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Assets;
using Chameleon.Core.Sessions;

namespace Chameleon.App.Projects
{
    /// <summary>Where projects (edit histories and their images) are kept.</summary>
    public interface IProjectStore
    {
        /// <summary>Loads a project, including changes not yet written; null when it does not exist.</summary>
        Task<SessionSnapshot?> LoadAsync(string projectId, CancellationToken cancellationToken);

        /// <summary>Saves a project soon; rapid changes are coalesced into one write.</summary>
        void ScheduleSave(SessionSnapshot snapshot);

        /// <summary>Writes every scheduled save now and waits for it.</summary>
        Task FlushAsync(CancellationToken cancellationToken);

        /// <summary>Image storage of a project.</summary>
        DiskAssetStore GetAssets(string projectId);

        /// <summary>Removes a project with its images, dropping any scheduled save.</summary>
        void DeleteProject(string projectId);

        /// <summary>Every stored project with the time it last changed.</summary>
        IReadOnlyList<KeyValuePair<string, DateTime>> List();
    }
}
