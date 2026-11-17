using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Imaging;

namespace Chameleon.App.Hosting
{
    /// <summary>
    /// Everything the application needs from Rhino and from the desktop shell. Implementations
    /// marshal to the UI thread themselves; callers may be on any thread.
    /// </summary>
    public interface IRhinoHost
    {
        /// <summary>Facts about the host process.</summary>
        HostInfo Info { get; }

        /// <summary>Captures the active viewport.</summary>
        /// <exception cref="RhinoHostException">There is no active view or the capture failed.</exception>
        Task<ImageData> CaptureActiveViewAsync(CaptureOptions options, CancellationToken cancellationToken);

        /// <summary>Lists the named views of the active document.</summary>
        Task<IReadOnlyList<string>> GetNamedViewsAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Captures a named view through the active viewport and restores the viewport's camera
        /// afterwards.
        /// </summary>
        /// <exception cref="RhinoHostException">The view does not exist or the capture failed.</exception>
        Task<ImageData> CaptureNamedViewAsync(string viewName, CaptureOptions options, CancellationToken cancellationToken);

        /// <summary>Shows a save dialog; returns the chosen path or null when cancelled.</summary>
        Task<string?> ShowSaveFileDialogAsync(SaveFileRequest request);

        /// <summary>Shows a folder picker; returns the chosen folder or null when cancelled.</summary>
        Task<string?> ShowSelectFolderDialogAsync();

        /// <summary>Shows an open dialog; returns the chosen paths (empty when cancelled).</summary>
        Task<IReadOnlyList<string>> ShowOpenFilesDialogAsync(OpenFilesRequest request);

        /// <summary>Opens a URL in the system browser.</summary>
        void OpenUrl(string url);

        /// <summary>Writes a diagnostic line to the host's log.</summary>
        void Log(string message);
    }
}
