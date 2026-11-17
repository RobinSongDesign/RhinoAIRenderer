using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ChameleonSpike
{
    /// <summary>Checks that run in the host process: capture, secret storage, object mask, token guard.</summary>
    public sealed class HostChecks
    {
        private readonly SpikeReport _report;

        /// <summary>Creates the checks, writing results into the report.</summary>
        public HostChecks(SpikeReport report)
        {
            _report = report;
        }

        /// <summary>Runs every host-side check, one after another. Call from a background thread.</summary>
        public async Task RunAllAsync(SpikeServer? server)
        {
            await RunAsync("A7", () => CheckWrongTokenAsync(server)).ConfigureAwait(false);
            await RunAsync("C1", CheckCaptureActiveViewAsync).ConfigureAwait(false);
            await RunAsync("C2", CheckCaptureSizesAsync).ConfigureAwait(false);
            await RunAsync("C3", CheckNamedViewAsync).ConfigureAwait(false);
            await RunAsync("C4", CheckCaptureFromWorkerThreadAsync).ConfigureAwait(false);
            await RunAsync("E1", CheckDpapiAsync).ConfigureAwait(false);
            await RunAsync("E2", CheckKeychainAsync).ConfigureAwait(false);
            await RunAsync("G1", CheckObjectMaskAsync).ConfigureAwait(false);
        }

        /// <summary>Runs one check, converting any exception into a failed result.</summary>
        public async Task RunAsync(string id, Func<Task<Outcome>> check)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                Outcome outcome = await check().ConfigureAwait(false);
                _report.Set(id, outcome.Passed, outcome.Detail, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _report.Set(id, false, ex.GetType().Name + ": " + ex.Message, stopwatch.ElapsedMilliseconds);
            }
        }

        // ── A7 ─────────────────────────────────────────────────────────────────

        private static async Task<Outcome> CheckWrongTokenAsync(SpikeServer? server)
        {
            if (server == null)
            {
                return Outcome.Skipped("server did not start");
            }

            using (var client = new HttpClient())
            {
                HttpResponseMessage response = await client
                    .GetAsync("http://127.0.0.1:" + server.Port + "/not-the-token/index.html")
                    .ConfigureAwait(false);
                int status = (int)response.StatusCode;
                return new Outcome(status == 404, "HTTP " + status);
            }
        }

        // ── C: capture ─────────────────────────────────────────────────────────

        private static Task<Outcome> CheckCaptureActiveViewAsync()
        {
            return OnUiThread(() =>
            {
                RhinoView view = RequireActiveView();
                Size size = view.ActiveViewport.Size;
                byte[] png = CapturePng(view, size.Width, size.Height, out Size actual);
                bool ok = IsPng(png) && actual == size;
                return new Outcome(ok, actual.Width + "x" + actual.Height + ", " + png.Length + " bytes, png=" + IsPng(png));
            });
        }

        private static Task<Outcome> CheckCaptureSizesAsync()
        {
            return OnUiThread(() =>
            {
                RhinoView view = RequireActiveView();
                var detail = new StringBuilder();
                bool ok = true;
                foreach (Size requested in new[] { new Size(1920, 1080), new Size(3840, 2160) })
                {
                    var stopwatch = Stopwatch.StartNew();
                    byte[] png = CapturePng(view, requested.Width, requested.Height, out Size actual);
                    ok &= actual == requested && IsPng(png);
                    detail.Append(requested.Width).Append('x').Append(requested.Height)
                          .Append(" -> ").Append(actual.Width).Append('x').Append(actual.Height)
                          .Append(", ").Append(png.Length).Append(" bytes, ")
                          .Append(stopwatch.ElapsedMilliseconds).Append(" ms; ");
                }

                return new Outcome(ok, detail.ToString());
            });
        }

        private static Task<Outcome> CheckNamedViewAsync()
        {
            return OnUiThread(() =>
            {
                RhinoDoc doc = RequireDoc();
                if (doc.NamedViews.Count == 0)
                {
                    return Outcome.Skipped("the document has no named views");
                }

                RhinoView view = RequireActiveView();
                RhinoViewport viewport = view.ActiveViewport;

                // Same save/restore logic as the product (legacy ScreenCapture.CaptureNamedView).
                Point3d savedTarget = viewport.CameraTarget;
                Point3d savedLocation = viewport.CameraLocation;
                Vector3d savedUp = viewport.CameraUp;
                double savedLens = viewport.Camera35mmLensLength;
                bool savedParallel = viewport.IsParallelProjection;

                byte[] png;
                try
                {
                    viewport.SetViewProjection(doc.NamedViews[0].Viewport, true);
                    view.Redraw();
                    png = CapturePng(view, viewport.Size.Width, viewport.Size.Height, out _);
                }
                finally
                {
                    if (savedParallel)
                    {
                        viewport.ChangeToParallelProjection(true);
                    }
                    else
                    {
                        viewport.ChangeToPerspectiveProjection(true, savedLens);
                    }

                    viewport.SetCameraLocations(savedTarget, savedLocation);
                    viewport.CameraUp = savedUp;
                    view.Redraw();
                }

                double drift = viewport.CameraLocation.DistanceTo(savedLocation) + viewport.CameraTarget.DistanceTo(savedTarget);
                bool restored = drift < 1e-6 && viewport.IsParallelProjection == savedParallel;
                return new Outcome(
                    IsPng(png) && restored,
                    "view '" + doc.NamedViews[0].Name + "', " + png.Length + " bytes, camera drift " + drift.ToString("0.########"));
            });
        }

        private static async Task<Outcome> CheckCaptureFromWorkerThreadAsync()
        {
            // The real bridge handles requests on thread-pool threads; this must not deadlock.
            Task<Outcome> capture = Task.Run(CheckCaptureActiveViewAsync);
            Task finished = await Task.WhenAny(capture, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
            if (finished != capture)
            {
                return new Outcome(false, "no result after 30 s (deadlock?)");
            }

            Outcome outcome = await capture.ConfigureAwait(false);
            return new Outcome(outcome.Passed, "from worker thread: " + outcome.Detail);
        }

        // ── E: secrets ─────────────────────────────────────────────────────────

        private static Task<Outcome> CheckDpapiAsync()
        {
            if (Rhino.Runtime.HostUtils.RunningOnOSX)
            {
                return Task.FromResult(Outcome.Skipped("Windows only"));
            }

            byte[] plain = Encoding.UTF8.GetBytes("spike-secret-" + Guid.NewGuid());
            byte[] entropy = Encoding.UTF8.GetBytes("ChameleonSpike");
            byte[] cipher = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);
            byte[] back = ProtectedData.Unprotect(cipher, entropy, DataProtectionScope.CurrentUser);
            return Task.FromResult(new Outcome(plain.SequenceEqual(back) && !plain.SequenceEqual(cipher), cipher.Length + " cipher bytes"));
        }

        private static Task<Outcome> CheckKeychainAsync()
        {
            if (!Rhino.Runtime.HostUtils.RunningOnOSX)
            {
                return Task.FromResult(Outcome.Skipped("macOS only"));
            }

            const string service = "ChameleonSpike";
            string account = "probe-" + Guid.NewGuid().ToString("N");
            string secret = "spike-secret-" + Guid.NewGuid();

            int add = MacKeychain.Add(service, account, secret);
            string? found = MacKeychain.Find(service, account, out int find);
            int delete = MacKeychain.Delete(service, account);
            string? afterDelete = MacKeychain.Find(service, account, out int findAfterDelete);

            bool ok = add == 0 && found == secret && delete == 0 && afterDelete == null;
            return Task.FromResult(new Outcome(
                ok,
                "add=" + add + " find=" + find + " match=" + (found == secret) + " delete=" + delete + " findAfterDelete=" + findAfterDelete +
                " (note in the report whether macOS showed an authorization dialog)"));
        }

        // ── G1: object mask ────────────────────────────────────────────────────

        private static Task<Outcome> CheckObjectMaskAsync()
        {
            return OnUiThread(() =>
            {
                RhinoDoc doc = RequireDoc();
                RhinoView view = RequireActiveView();
                Guid[] selected = doc.Objects.GetSelectedObjects(false, false).Select(o => o.Id).ToArray();
                if (selected.Length == 0)
                {
                    return Outcome.Skipped("select a few objects before running the spike");
                }

                // Render meshes must exist before the conduit draws; creating them inside a draw
                // callback is not safe.
                foreach (RhinoObject obj in doc.Objects)
                {
                    Mesh[] meshes = obj.GetMeshes(MeshType.Render);
                    if (meshes == null || meshes.Length == 0)
                    {
                        obj.CreateMeshes(MeshType.Render, MeshingParameters.FastRenderMesh, false);
                    }
                }

                Size size = view.ActiveViewport.Size;
                byte[] beauty = CapturePng(view, size.Width, size.Height, out _);

                double white, black, other;
                byte[] mask;
                var conduit = new MaskConduit(selected) { Enabled = true };
                try
                {
                    using (Bitmap bitmap = Capture(view, size.Width, size.Height))
                    {
                        Classify(bitmap, out white, out black, out other);
                        mask = ToPng(bitmap);
                    }
                }
                finally
                {
                    conduit.Enabled = false;
                    view.Redraw();
                }

                string desktop = SpikeReport.DesktopDirectory();
                File.WriteAllBytes(Path.Combine(desktop, "chameleon-spike-beauty.png"), beauty);
                File.WriteAllBytes(Path.Combine(desktop, "chameleon-spike-mask.png"), mask);

                // A usable mask is almost purely black and white, with some of each.
                bool ok = white > 0.001 && black > 0.05 && other < 0.05;
                return new Outcome(
                    ok,
                    selected.Length + " selected; white " + white.ToString("P1") + ", black " + black.ToString("P1") +
                    ", other " + other.ToString("P1") + "; images saved to the desktop for visual comparison");
            });
        }

        private static void Classify(Bitmap bitmap, out double white, out double black, out double other)
        {
            int w = 0, b = 0, o = 0;
            int stepX = Math.Max(1, bitmap.Width / 96);
            int stepY = Math.Max(1, bitmap.Height / 96);
            for (int y = 0; y < bitmap.Height; y += stepY)
            {
                for (int x = 0; x < bitmap.Width; x += stepX)
                {
                    Color c = bitmap.GetPixel(x, y);
                    int min = Math.Min(c.R, Math.Min(c.G, c.B));
                    int max = Math.Max(c.R, Math.Max(c.G, c.B));
                    if (min >= 240)
                    {
                        w++;
                    }
                    else if (max <= 15)
                    {
                        b++;
                    }
                    else
                    {
                        o++;
                    }
                }
            }

            double total = Math.Max(1, w + b + o);
            white = w / total;
            black = b / total;
            other = o / total;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static Bitmap Capture(RhinoView view, int width, int height)
        {
            var capture = new ViewCapture
            {
                Width = width,
                Height = height,
                TransparentBackground = false,
                ScaleScreenItems = false,
                DrawAxes = false,
                DrawGrid = false,
                DrawGridAxes = false
            };

            return capture.CaptureToBitmap(view) ?? throw new InvalidOperationException("ViewCapture returned null.");
        }

        private static byte[] CapturePng(RhinoView view, int width, int height, out Size actualSize)
        {
            using (Bitmap bitmap = Capture(view, width, height))
            {
                actualSize = bitmap.Size;
                return ToPng(bitmap);
            }
        }

        private static byte[] ToPng(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        private static bool IsPng(byte[] bytes)
        {
            return bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        }

        private static RhinoDoc RequireDoc()
        {
            return RhinoDoc.ActiveDoc ?? throw new InvalidOperationException("No active document.");
        }

        private static RhinoView RequireActiveView()
        {
            return RequireDoc().Views.ActiveView ?? throw new InvalidOperationException("No active view.");
        }

        private static Task<Outcome> OnUiThread(Func<Outcome> work)
        {
            var completion = new TaskCompletionSource<Outcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                try
                {
                    completion.TrySetResult(work());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }));
            return completion.Task;
        }

        /// <summary>Result of a single check.</summary>
        public readonly struct Outcome
        {
            /// <summary>Creates an outcome.</summary>
            public Outcome(bool? passed, string detail)
            {
                Passed = passed;
                Detail = detail;
            }

            /// <summary>True, false, or null when the check could not run.</summary>
            public bool? Passed { get; }

            /// <summary>Measured values or the reason for skipping.</summary>
            public string Detail { get; }

            /// <summary>An outcome for a check that did not apply.</summary>
            public static Outcome Skipped(string reason) => new Outcome(null, "skipped: " + reason);
        }
    }
}
