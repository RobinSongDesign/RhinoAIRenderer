using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Hosting;
using Chameleon.App.Platform;
using Chameleon.Core.Imaging;
using Eto.Forms;
using Rhino;

namespace Chameleon.Rhino.Hosting
{
    /// <summary><see cref="IRhinoHost"/> for a real Rhino. Every Rhino and Eto call is marshalled to the UI thread.</summary>
    internal sealed class RhinoHost : IRhinoHost
    {
        private readonly FileLogger _logger;

        public RhinoHost(HostPlatform platform, string appVersion, FileLogger logger)
        {
            Info = new HostInfo(platform, RhinoApp.Version.ToString(), appVersion);
            _logger = logger;
        }

        public HostInfo Info { get; }

        /// <summary>Window that native dialogs are parented to; the main Rhino window when null.</summary>
        public Window? DialogParent { get; set; }

        public Task<ImageData> CaptureActiveViewAsync(CaptureOptions options, CancellationToken cancellationToken)
        {
            return UiThread.InvokeAsync(() => ViewCapturer.CaptureActiveView(options));
        }

        public Task<IReadOnlyList<string>> GetNamedViewsAsync(CancellationToken cancellationToken)
        {
            return UiThread.InvokeAsync<IReadOnlyList<string>>(ViewCapturer.NamedViewNames);
        }

        public Task<ImageData> CaptureNamedViewAsync(string viewName, CaptureOptions options, CancellationToken cancellationToken)
        {
            return UiThread.InvokeAsync(() => ViewCapturer.CaptureNamedView(viewName, options));
        }

        public Task<string?> ShowSaveFileDialogAsync(SaveFileRequest request)
        {
            return UiThread.InvokeAsync<string?>(() =>
            {
                var dialog = new SaveFileDialog { FileName = request.SuggestedFileName };
                dialog.Filters.Add(new FileFilter(request.Extension.ToUpperInvariant(), "." + request.Extension));
                return dialog.ShowDialog(Parent()) == DialogResult.Ok ? dialog.FileName : null;
            });
        }

        public Task<string?> ShowSelectFolderDialogAsync()
        {
            return UiThread.InvokeAsync<string?>(() =>
            {
                var dialog = new SelectFolderDialog();
                return dialog.ShowDialog(Parent()) == DialogResult.Ok ? dialog.Directory : null;
            });
        }

        public Task<IReadOnlyList<string>> ShowOpenFilesDialogAsync(OpenFilesRequest request)
        {
            return UiThread.InvokeAsync<IReadOnlyList<string>>(() =>
            {
                var dialog = new OpenFileDialog { MultiSelect = request.Multiple };
                if (request.Extensions.Count > 0)
                {
                    dialog.Filters.Add(new FileFilter("Images", request.Extensions.Select(e => "." + e).ToArray()));
                }

                return dialog.ShowDialog(Parent()) == DialogResult.Ok
                    ? (request.Multiple ? dialog.Filenames.ToArray() : new[] { dialog.FileName })
                    : new string[0];
            });
        }

        public void OpenUrl(string url)
        {
            try
            {
                if (Info.Platform == HostPlatform.MacOS)
                {
                    Process.Start("open", url);
                }
                else
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Log("Opening " + url + " failed: " + ex.Message);
            }
        }

        public void Log(string message)
        {
            _logger.Write(message);

            // Failures also get a one-line summary where Rhino users look first.
            if (message != null && message.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string firstLine = message.Split('\n')[0].Trim();
                RhinoApp.WriteLine("Chameleon: " + firstLine);
            }
        }

        private Window Parent()
        {
            return DialogParent ?? global::Rhino.UI.RhinoEtoApp.MainWindow;
        }
    }
}
