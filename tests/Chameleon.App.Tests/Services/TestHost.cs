using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Hosting;
using Chameleon.Core.Imaging;

namespace Chameleon.App.Tests.Services
{
    /// <summary>Scriptable IRhinoHost for service tests.</summary>
    internal sealed class TestHost : IRhinoHost
    {
        public HostInfo Info { get; set; } = new HostInfo(HostPlatform.Other, "8.36", "2.0.0-test");

        public List<string> NamedViews { get; } = new List<string> { "Exterior", "Interior" };

        public string? SaveDialogResult { get; set; }

        public string? FolderDialogResult { get; set; }

        public List<string> OpenDialogResult { get; } = new List<string>();

        public List<string> OpenedUrls { get; } = new List<string>();

        public List<string> LogLines { get; } = new List<string>();

        public List<SaveFileRequest> SaveRequests { get; } = new List<SaveFileRequest>();

        public CaptureOptions? LastCaptureOptions { get; private set; }

        public bool HasActiveView { get; set; } = true;

        public Task<ImageData> CaptureActiveViewAsync(CaptureOptions options, CancellationToken cancellationToken)
        {
            LastCaptureOptions = options;
            if (!HasActiveView)
            {
                throw new RhinoHostException("No active view.");
            }

            return Task.FromResult(TestImages.Png());
        }

        public Task<IReadOnlyList<string>> GetNamedViewsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<string>>(NamedViews.ToArray());
        }

        public Task<ImageData> CaptureNamedViewAsync(string viewName, CaptureOptions options, CancellationToken cancellationToken)
        {
            LastCaptureOptions = options;
            if (!NamedViews.Contains(viewName))
            {
                throw new RhinoHostException("Named view '" + viewName + "' not found.");
            }

            return Task.FromResult(viewName == "Exterior" ? TestImages.Jpeg() : TestImages.Webp());
        }

        public Task<string?> ShowSaveFileDialogAsync(SaveFileRequest request)
        {
            SaveRequests.Add(request);
            return Task.FromResult(SaveDialogResult);
        }

        public Task<string?> ShowSelectFolderDialogAsync() => Task.FromResult(FolderDialogResult);

        public Task<IReadOnlyList<string>> ShowOpenFilesDialogAsync(OpenFilesRequest request)
        {
            return Task.FromResult<IReadOnlyList<string>>(OpenDialogResult.ToArray());
        }

        public void OpenUrl(string url) => OpenedUrls.Add(url);

        public void Log(string message)
        {
            lock (LogLines)
            {
                LogLines.Add(message);
            }
        }
    }
}
