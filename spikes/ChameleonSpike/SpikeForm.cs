using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino;

namespace ChameleonSpike
{
    /// <summary>
    /// Window hosting the test page. Loads the page over loopback HTTP first; when that does not
    /// work it falls back to LoadHtml so the Eto message channel can still be evaluated.
    /// </summary>
    public sealed class SpikeForm : Form
    {
        private const int HttpLoadTimeoutMs = 8000;
        private const string MessageSeparator = "\n";

        private readonly SpikeReport _report = new SpikeReport();
        private readonly WebView _webView = new WebView();
        private readonly string _pageHtml = ReadEmbeddedPage();
        private readonly Stopwatch _sinceStart = new Stopwatch();

        private SpikeServer? _server;
        private bool _pageSaidHello;
        private bool _hostChecksStarted;
        private TaskCompletionSource<long>? _bigMessage;

        /// <summary>Creates the window; checks start when it is shown.</summary>
        public SpikeForm()
        {
            Title = "Chameleon Spike";
            ClientSize = new Size(1100, 820);
            Owner = Rhino.UI.RhinoEtoApp.MainWindow;
            Content = _webView;

            _webView.MessageReceived += (sender, e) => OnEtoMessage(e.Message);
            _report.Changed += (sender, e) => PushResultsToPage();

            Shown += (sender, e) => Start();
            Closed += (sender, e) =>
            {
                TrySaveReport();
                _server?.Dispose();
            };
        }

        // ── Start-up ───────────────────────────────────────────────────────────

        private void Start()
        {
            _sinceStart.Start();
            try
            {
                _server = new SpikeServer(_pageHtml, HandleCommand, HandlePageReport);
                _report.Set("A1", true, "listening on 127.0.0.1:" + _server.Port);
                _webView.Url = new Uri(_server.BaseUrl);
            }
            catch (Exception ex)
            {
                _report.Set("A1", false, ex.GetType().Name + ": " + ex.Message);
            }

            // If the page never reports in over HTTP, load it directly so the remaining checks can run.
            Task.Delay(HttpLoadTimeoutMs).ContinueWith(_ => Application.Instance.AsyncInvoke(FallBackToLoadHtmlIfNeeded));
        }

        private void FallBackToLoadHtmlIfNeeded()
        {
            if (_pageSaidHello || IsDisposed)
            {
                return;
            }

            _report.Set("A2", false, "page did not load over http within " + HttpLoadTimeoutMs + " ms; reloaded with LoadHtml");
            try
            {
                string directory = Path.Combine(Path.GetTempPath(), "ChameleonSpike");
                Directory.CreateDirectory(directory);
                _webView.LoadHtml(_pageHtml, new Uri(directory + Path.DirectorySeparatorChar));
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("ChameleonSpike: LoadHtml fallback failed: " + ex.Message);
                StartHostChecks();
            }
        }

        private void OnPageHello(string mode)
        {
            if (_pageSaidHello)
            {
                return;
            }

            _pageSaidHello = true;
            if (mode == "http")
            {
                _report.Set("A2", true, "page script ran", _sinceStart.ElapsedMilliseconds);
            }

            Application.Instance.AsyncInvoke(async () =>
            {
                await RunEtoChannelChecksAsync();
                StartHostChecks();
            });
        }

        private void StartHostChecks()
        {
            if (_hostChecksStarted)
            {
                return;
            }

            _hostChecksStarted = true;
            SpikeServer? server = _server;
            Task.Run(async () =>
            {
                await new HostChecks(_report).RunAllAsync(server).ConfigureAwait(false);
                TrySaveReport();
            });
        }

        // ── B: Eto message channel (runs on the UI thread) ─────────────────────

        private async Task RunEtoChannelChecksAsync()
        {
            // B1 is recorded when the page's ping arrives; give it a moment before declaring failure.
            await Task.Delay(1500);
            if (!_report.Has("B1"))
            {
                _report.Set("B1", false, "no window.eto.postMessage message received");
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                string result = await _webView.ExecuteScriptAsync("1+1");
                _report.Set("B2", result != null && result.Trim().Trim('"') == "2", "returned '" + result + "'", stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _report.Set("B2", false, ex.GetType().Name + ": " + ex.Message, stopwatch.ElapsedMilliseconds);
            }

            var detail = new StringBuilder();
            bool allOk = true;
            foreach (int size in new[] { 1024 * 1024, 10 * 1024 * 1024 })
            {
                long elapsed = await SendBigMessageAsync(size);
                allOk &= elapsed >= 0;
                detail.Append(size / (1024 * 1024)).Append(" MB: ").Append(elapsed >= 0 ? elapsed + " ms" : "not received").Append("; ");
            }

            _report.Set("B3", allOk, detail.ToString());
            await RunLoadHtmlCheckAsync();
        }

        private async Task<long> SendBigMessageAsync(int size)
        {
            _bigMessage = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await _webView.ExecuteScriptAsync("window.__spike.sendBig(" + size + ")");
                Task finished = await Task.WhenAny(_bigMessage.Task, Task.Delay(20000));
                if (finished == _bigMessage.Task && await _bigMessage.Task == size)
                {
                    return stopwatch.ElapsedMilliseconds;
                }
            }
            catch (Exception)
            {
                // Reported as "not received".
            }

            return -1;
        }

        // B4: a second WebView loads a page with LoadHtml and shows an image from the same folder.
        private async Task RunLoadHtmlCheckAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Form? window = null;
            try
            {
                string directory = Path.Combine(Path.GetTempPath(), "ChameleonSpike");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, "b4.png"), PngWriter.Gradient(256, 256));

                var webView = new WebView();
                webView.MessageReceived += (sender, e) => result.TrySetResult(e.Message);
                window = new Form { Title = "Spike B4", ClientSize = new Size(320, 320), Content = webView, Owner = this };
                window.Show();

                const string html =
                    "<!doctype html><html><body style='margin:0;background:#222'>" +
                    "<img src='b4.png' width='256' height='256' " +
                    "onload=\"window.eto&&window.eto.postMessage('b4-loaded '+this.naturalWidth)\" " +
                    "onerror=\"window.eto&&window.eto.postMessage('b4-error')\"></body></html>";
                webView.LoadHtml(html, new Uri(directory + Path.DirectorySeparatorChar));

                Task finished = await Task.WhenAny(result.Task, Task.Delay(8000));
                if (finished == result.Task)
                {
                    string message = await result.Task;
                    _report.Set("B4", message == "b4-loaded 256", message, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    _report.Set("B4", false, "no message from the LoadHtml page within 8 s", stopwatch.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                _report.Set("B4", false, ex.GetType().Name + ": " + ex.Message, stopwatch.ElapsedMilliseconds);
            }
            finally
            {
                window?.Close();
            }
        }

        // ── Messages from the page ─────────────────────────────────────────────

        // Eto channel format: "<kind>\n<rest>". Kinds: b1, big, report, cmd.
        private void OnEtoMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            int split = message.IndexOf(MessageSeparator, StringComparison.Ordinal);
            string kind = split < 0 ? message : message.Substring(0, split);
            string rest = split < 0 ? string.Empty : message.Substring(split + 1);

            switch (kind)
            {
                case "b1":
                    _report.Set("B1", true, "received '" + rest + "'");
                    break;
                case "big":
                    _bigMessage?.TrySetResult(rest.Length);
                    break;
                case "report":
                    HandlePageReport(rest);
                    break;
                case "cmd":
                {
                    // "<id>\n<name>\n<argument>"
                    string[] parts = rest.Split(new[] { '\n' }, 3);
                    if (parts.Length >= 2)
                    {
                        string id = parts[0];
                        string name = parts[1];
                        string argument = parts.Length > 2 ? parts[2] : string.Empty;
                        Task.Run(() =>
                        {
                            string result = HandleCommand(name, argument);
                            ExecuteScript("window.__spike.onCommandResult(" + JsonConvert.ToString(id) + "," + JsonConvert.ToString(result) + ")");
                        });
                    }

                    break;
                }
            }
        }

        private void HandlePageReport(string json)
        {
            try
            {
                JObject result = JObject.Parse(json);
                string id = (string?)result["id"] ?? string.Empty;
                if (Array.IndexOf(SpikeReport.AllIds, id) >= 0)
                {
                    _report.Set(id, (bool?)result["passed"], (string?)result["detail"] ?? string.Empty, (long?)result["durationMs"] ?? 0);
                }
            }
            catch (JsonException)
            {
                // A malformed report from the page is ignored.
            }
        }

        // Called from HTTP worker threads and from the Eto channel; anything touching UI is marshalled.
        private string HandleCommand(string name, string argument)
        {
            try
            {
                switch (name)
                {
                    case "hello":
                        OnPageHello(argument);
                        return "ok";
                    case "echo":
                        return argument;
                    case "dialog.save":
                        return Application.Instance.Invoke(() =>
                        {
                            var dialog = new SaveFileDialog { Title = "Spike: save file dialog", FileName = "spike-test.png" };
                            dialog.Filters.Add(new FileFilter("PNG image", ".png"));
                            return dialog.ShowDialog(this) == DialogResult.Ok ? dialog.FileName : "(cancelled)";
                        });
                    case "dialog.folder":
                        return Application.Instance.Invoke(() =>
                        {
                            var dialog = new SelectFolderDialog { Title = "Spike: select folder dialog" };
                            return dialog.ShowDialog(this) == DialogResult.Ok ? dialog.Directory : "(cancelled)";
                        });
                    case "dialog.open":
                        return Application.Instance.Invoke(() =>
                        {
                            var dialog = new OpenFileDialog { Title = "Spike: open file dialog", MultiSelect = true };
                            dialog.Filters.Add(new FileFilter("Images", ".png", ".jpg", ".jpeg", ".webp"));
                            return dialog.ShowDialog(this) == DialogResult.Ok ? string.Join("; ", dialog.Filenames) : "(cancelled)";
                        });
                    case "open.url":
                        OpenInBrowser("https://www.rhino3d.com");
                        return "requested";
                    case "save.report":
                        return _report.Save();
                    default:
                        return "unknown command: " + name;
                }
            }
            catch (Exception ex)
            {
                return "error: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static void OpenInBrowser(string url)
        {
            if (Rhino.Runtime.HostUtils.RunningOnOSX)
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        }

        // ── Host → page ────────────────────────────────────────────────────────

        private void PushResultsToPage()
        {
            ExecuteScript("window.__spike && window.__spike.setResults(" + _report.ToJson() + ")");
        }

        private void ExecuteScript(string script)
        {
            Application.Instance.AsyncInvoke(() =>
            {
                if (IsDisposed)
                {
                    return;
                }

                try
                {
                    _ = _webView.ExecuteScriptAsync(script);
                }
                catch (Exception)
                {
                    // The page may not be ready yet; the next change pushes the full state again.
                }
            });
        }

        private void TrySaveReport()
        {
            try
            {
                RhinoApp.WriteLine("ChameleonSpike report saved to " + _report.Save());
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("ChameleonSpike could not save the report: " + ex.Message);
            }
        }

        private static string ReadEmbeddedPage()
        {
            Assembly assembly = typeof(SpikeForm).Assembly;
            using (Stream? stream = assembly.GetManifestResourceStream("ChameleonSpike.web.index.html"))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Embedded page is missing.");
                }

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
