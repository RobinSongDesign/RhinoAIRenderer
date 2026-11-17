using System;
using Chameleon.Rhino.Hosting;
using Eto.Drawing;
using Eto.Forms;

namespace Chameleon.Rhino.UI
{
    /// <summary>The Chameleon window: a WebView showing the UI served by the application's local server.</summary>
    internal sealed class MainForm : Form
    {
        private static readonly Size DefaultSize = new Size(1280, 820);
        private static readonly Size MinimumClientSize = new Size(900, 600);

        private readonly string _baseUrl;
        private readonly RhinoHost _host;
        private readonly WindowPlacement _placement;

        public MainForm(string baseUrl, RhinoHost host, WindowPlacement placement)
        {
            _baseUrl = baseUrl;
            _host = host;
            _placement = placement;

            Title = "Chameleon";
            MinimumSize = MinimumClientSize;
            Owner = global::Rhino.UI.RhinoEtoApp.MainWindow;

            Rectangle? saved = placement.Load(MinimumClientSize);
            if (saved.HasValue)
            {
                Location = saved.Value.Location;
                ClientSize = saved.Value.Size;
            }
            else
            {
                ClientSize = DefaultSize;
            }

            var webView = new WebView();
#if !DEBUG
            webView.BrowserContextMenuEnabled = false;
#endif
            webView.DocumentLoading += OnDocumentLoading;
            webView.OpenNewWindow += OnOpenNewWindow;
            webView.Url = new Uri(baseUrl);
            Content = webView;

            Shown += (sender, e) => _host.DialogParent = this;
            Closing += (sender, e) => _placement.Save(new Rectangle(Location, ClientSize));
            Closed += (sender, e) =>
            {
                if (ReferenceEquals(_host.DialogParent, this))
                {
                    _host.DialogParent = null;
                }
            };
        }

        // The window only ever shows our own UI; links to anything else open in the system browser.
        private void OnDocumentLoading(object? sender, WebViewLoadingEventArgs e)
        {
            if (e.Uri == null || e.Uri.AbsoluteUri.StartsWith(_baseUrl, StringComparison.Ordinal) || e.Uri.Scheme == "about")
            {
                return;
            }

            e.Cancel = true;
            OpenExternally(e.Uri);
        }

        private void OnOpenNewWindow(object? sender, WebViewNewWindowEventArgs e)
        {
            e.Cancel = true;
            if (e.Uri != null)
            {
                OpenExternally(e.Uri);
            }
        }

        private void OpenExternally(Uri uri)
        {
            if (uri.Scheme == Uri.UriSchemeHttps)
            {
                _host.OpenUrl(uri.AbsoluteUri);
            }
        }
    }
}
