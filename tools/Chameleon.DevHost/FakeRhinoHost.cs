using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Hosting;
using Chameleon.Core.Imaging;

namespace Chameleon.DevHost
{
    /// <summary>
    /// Stand-in for Rhino: captures return sample images, dialogs answer immediately with paths
    /// inside an output folder, and "opening a URL" is only logged.
    /// </summary>
    public sealed class FakeRhinoHost : IRhinoHost
    {
        private readonly SampleImages _samples;
        private readonly string _outputDirectory;
        private readonly TextWriter _log;

        /// <summary>Creates the fake host.</summary>
        /// <param name="samples">Source of capture images.</param>
        /// <param name="outputDirectory">Folder the fake save dialogs point into.</param>
        /// <param name="log">Where diagnostics go.</param>
        public FakeRhinoHost(SampleImages samples, string outputDirectory, TextWriter log)
        {
            _samples = samples;
            _outputDirectory = outputDirectory;
            _log = log;
            Directory.CreateDirectory(outputDirectory);
        }

        /// <inheritdoc />
        public HostInfo Info { get; } = new HostInfo(HostPlatform.Other, string.Empty, "dev");

        /// <summary>URLs the UI asked to open, most recent last. Inspected by end-to-end tests through the log.</summary>
        public IList<string> OpenedUrls { get; } = new List<string>();

        /// <inheritdoc />
        public Task<ImageData> CaptureActiveViewAsync(CaptureOptions options, CancellationToken cancellationToken)
        {
            return Task.FromResult(_samples.Capture());
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> GetNamedViewsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_samples.NamedViews);
        }

        /// <inheritdoc />
        public Task<ImageData> CaptureNamedViewAsync(string viewName, CaptureOptions options, CancellationToken cancellationToken)
        {
            if (!_samples.NamedViews.Contains(viewName))
            {
                throw new RhinoHostException("Named view '" + viewName + "' not found.");
            }

            return Task.FromResult(_samples.Capture(viewName));
        }

        /// <inheritdoc />
        public Task<string?> ShowSaveFileDialogAsync(SaveFileRequest request)
        {
            return Task.FromResult<string?>(Path.Combine(_outputDirectory, request.SuggestedFileName));
        }

        /// <inheritdoc />
        public Task<string?> ShowSelectFolderDialogAsync()
        {
            return Task.FromResult<string?>(_outputDirectory);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ShowOpenFilesDialogAsync(OpenFilesRequest request)
        {
            return Task.FromResult<IReadOnlyList<string>>(new[] { _samples.ReferencePath });
        }

        /// <inheritdoc />
        public void OpenUrl(string url)
        {
            OpenedUrls.Add(url);
            Log("open url: " + url);
        }

        /// <inheritdoc />
        public void Log(string message)
        {
            _log.WriteLine("[host] " + message);
        }
    }
}
