using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Chameleon.Core.Generation;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Chameleon.App.Tests.Services
{
    public class CaptureJobFileServiceTests : IDisposable
    {
        private readonly TestApp _app = new TestApp();

        public void Dispose() => _app.Dispose();

        private async Task<string> CaptureAsync()
        {
            return (string)(await _app.CallAsync("rhino.captureActiveView"))["assetId"]!;
        }

        private static object Job(string source, string prompt = "render", string backend = "scripted", string model = "scripted-a")
        {
            return new { backendId = backend, modelId = model, prompt, sourceAssetId = source, imageSize = "1K" };
        }

        private Task<JToken> WaitForJobAsync(string jobId, string state)
        {
            return _app.WaitForEventAsync("job.updated", j => (string)j["id"]! == jobId && (string)j["state"]! == state);
        }

        // ── capture ────────────────────────────────────────────────────────────

        [Fact]
        public async Task CaptureActiveView_ReturnsAnAssetTheUiCanDownload()
        {
            JToken asset = await _app.CallAsync("rhino.captureActiveView", new { width = 1920, height = 1080 });

            Assert.Equal("image/png", (string)asset["mimeType"]!);
            Assert.Equal(64, (int)asset["width"]!);
            Assert.Equal(TestImages.Png().Bytes, await _app.DownloadAsync((string)asset["assetId"]!));
            Assert.Equal(1920, _app.Host.LastCaptureOptions!.Width);
            Assert.Equal(1080, _app.Host.LastCaptureOptions.Height);
        }

        [Fact]
        public async Task CaptureActiveView_WithoutParameters_UsesTheViewportSize()
        {
            await _app.CallAsync("rhino.captureActiveView");

            Assert.Null(_app.Host.LastCaptureOptions!.Width);
            Assert.Null(_app.Host.LastCaptureOptions.Height);
        }

        [Fact]
        public async Task CaptureFailures_AreRhinoErrorsWithAReadableDetail()
        {
            _app.Host.HasActiveView = false;

            JObject noView = await _app.RpcAsync("rhino.captureActiveView");
            JObject noNamedView = await _app.RpcAsync("rhino.captureNamedView", new { name = "Missing" });

            Assert.Equal("rhino_error", (string)noView["error"]!["code"]!);
            Assert.Equal("No active view.", (string)noView["error"]!["detail"]!);
            Assert.Equal("rhino_error", (string)noNamedView["error"]!["code"]!);
            Assert.Contains("Missing", (string)noNamedView["error"]!["detail"]!);
        }

        [Fact]
        public async Task NamedViews_CanBeListedAndCaptured()
        {
            JToken views = await _app.CallAsync("rhino.listNamedViews");
            JToken exterior = await _app.CallAsync("rhino.captureNamedView", new { name = "Exterior" });

            Assert.Equal(new[] { "Exterior", "Interior" }, views.Select(v => (string)v["name"]!).ToArray());
            Assert.Equal("image/jpeg", (string)exterior["mimeType"]!);
        }

        [Theory]
        [InlineData(0, 100)]
        [InlineData(100, -5)]
        [InlineData(20000, 100)]
        public async Task Capture_RejectsUnreasonableSizes(int width, int height)
        {
            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("rhino.captureActiveView", new { width, height }));
            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("rhino.captureNamedView", new { name = "" }));
        }

        // ── jobs ───────────────────────────────────────────────────────────────

        [Fact]
        public async Task Submit_RunsTheJob_AndPublishesItsResultAsAnAsset()
        {
            string source = await CaptureAsync();

            JToken job = await _app.CallAsync("jobs.submit", new
            {
                backendId = "scripted", modelId = "scripted-a", prompt = "render", addonPrompt = "at dusk",
                sourceAssetId = source, imageSize = "2K", aspectRatio = "16:9", label = "Hero",
                references = new[] { new { assetId = source, role = "style", note = "like this" } }
            });
            JToken done = await WaitForJobAsync((string)job["id"]!, "succeeded");

            Assert.Equal("queued", (string)job["state"]!);
            Assert.Equal("Hero", (string)done["label"]!);
            JToken asset = done["result"]!["assets"]![0]!;
            Assert.Equal("image/jpeg", (string)asset["mimeType"]!);
            Assert.Equal(TestImages.Jpeg().Bytes, await _app.DownloadAsync((string)asset["assetId"]!));
            Assert.Equal("done", (string)done["result"]!["text"]!);
            Assert.Equal(4, (int)done["result"]!["creditsCharged"]!);

            GenerationRequest sent = _app.Backend.Requests.Single();
            Assert.Equal("render\n\nat dusk", sent.Prompt);
            Assert.Equal("2K", sent.ImageSize);
            Assert.Equal("16:9", sent.AspectRatio);
            Assert.Equal(ReferenceRole.Style, sent.References.Single().Role);
            Assert.Equal("like this", sent.References.Single().Note);
            // No explicit system prompt: the catalog default applies.
            Assert.StartsWith("This is a render image.", sent.SystemPrompt);
        }

        [Fact]
        public async Task Submit_SystemPromptPrecedence_ExplicitThenUserSettingThenCatalog()
        {
            string source = await CaptureAsync();
            await _app.CallAsync("settings.update", new { systemPrompt = "user setting" });

            JToken a = await _app.CallAsync("jobs.submit", Job(source));
            await WaitForJobAsync((string)a["id"]!, "succeeded");
            JToken b = await _app.CallAsync("jobs.submit", new { backendId = "scripted", modelId = "scripted-a", prompt = "p", sourceAssetId = source, imageSize = "1K", systemPrompt = "explicit" });
            await WaitForJobAsync((string)b["id"]!, "succeeded");

            Assert.Equal(new[] { "user setting", "explicit" }, _app.Backend.Requests.Select(r => r.SystemPrompt).ToArray());
        }

        [Fact]
        public async Task Submit_FailedJob_ReportsTheMappedErrorWithProviderDetail()
        {
            string source = await CaptureAsync();

            JToken job = await _app.CallAsync("jobs.submit", Job(source, "#fail"));
            JToken failed = await WaitForJobAsync((string)job["id"]!, "failed");

            Assert.Equal("rate_limited", (string)failed["error"]!["code"]!);
            Assert.Equal("slow down please", (string)failed["error"]!["detail"]!);
            Assert.True((bool)failed["error"]!["retryable"]!);
            Assert.Null(failed["result"]);
        }

        [Fact]
        public async Task Cancel_AbortsARunningJob()
        {
            string source = await CaptureAsync();
            JToken job = await _app.CallAsync("jobs.submit", Job(source, "#hold"));
            await WaitForJobAsync((string)job["id"]!, "running");

            await _app.CallAsync("jobs.cancel", new { jobId = (string)job["id"]! });
            JToken cancelled = await WaitForJobAsync((string)job["id"]!, "cancelled");

            Assert.Equal("cancelled", (string)cancelled["error"]!["code"]!);
            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("jobs.cancel", new { }));
        }

        [Fact]
        public async Task Submit_ToABackendWithoutKey_IsNotReady_AndUnknownThingsAreNotFound()
        {
            string source = await CaptureAsync();

            Assert.Equal("not_ready", await _app.ErrorCodeAsync("jobs.submit", Job(source, backend: "direct:Chameleon", model: "gemini-3-pro-image")));
            Assert.Equal("not_found", await _app.ErrorCodeAsync("jobs.submit", Job(source, backend: "direct:nope")));
            Assert.Equal("not_found", await _app.ErrorCodeAsync("jobs.submit", Job(new string('a', 64))));
            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("jobs.submit", new
            {
                backendId = "scripted", modelId = "scripted-a", prompt = "p", sourceAssetId = source,
                references = new[] { new { assetId = source, role = "vibes" } }
            }));
            Assert.Empty(_app.Backend.Requests);
        }

        [Fact]
        public async Task Submit_ThroughADirectProvider_SendsTheStoredKeyAndReturnsTheImage()
        {
            string source = await CaptureAsync();
            await _app.CallAsync("providers.setKey", new { id = "Chameleon", key = TestApp.GoodKey });

            JToken job = await _app.CallAsync("jobs.submit", Job(source, backend: "direct:Chameleon", model: "gemini-3-pro-image"));
            JToken done = await WaitForJobAsync((string)job["id"]!, "succeeded");

            Assert.Equal("image/webp", (string)done["result"]!["assets"]![0]!["mimeType"]!);
            Assert.Equal(TestApp.GoodKey, _app.ProviderRequests.Last().Headers.Authorization!.Parameter);
        }

        [Fact]
        public async Task SubmitBatch_Serial_ChainsResultsAsConsistencyReferences()
        {
            string exterior = (string)(await _app.CallAsync("rhino.captureNamedView", new { name = "Exterior" }))["assetId"]!;
            string interior = (string)(await _app.CallAsync("rhino.captureNamedView", new { name = "Interior" }))["assetId"]!;

            JToken batch = await _app.CallAsync("jobs.submitBatch", new
            {
                mode = "serial",
                items = new object[]
                {
                    new { backendId = "scripted", modelId = "scripted-a", prompt = "shared", sourceAssetId = exterior, imageSize = "1K", label = "Exterior" },
                    new { backendId = "scripted", modelId = "scripted-a", prompt = "shared", addonPrompt = "warm light", sourceAssetId = interior, imageSize = "1K", label = "Interior" }
                }
            });
            string second = (string)batch["jobs"]![1]!["id"]!;
            await WaitForJobAsync(second, "succeeded");
            JToken list = await _app.CallAsync("jobs.list");

            Assert.Equal(32, ((string)batch["groupId"]!).Length);
            Assert.Equal(new[] { "Exterior", "Interior" }, batch["jobs"]!.Select(j => (string)j["label"]!).ToArray());
            Assert.All(batch["jobs"]!, j => Assert.Equal((string)batch["groupId"]!, (string)j["groupId"]!));
            Assert.Equal(2, list.Count());
            Assert.All(list, j => Assert.Equal("succeeded", (string)j["state"]!));

            Assert.Empty(_app.Backend.Requests[0].References);
            Assert.Equal("shared\n\nwarm light", _app.Backend.Requests[1].Prompt);
            Assert.Equal(ReferenceRole.Consistency, _app.Backend.Requests[1].References.Single().Role);
        }

        [Fact]
        public async Task SubmitBatch_CancelGroup_StopsEverything()
        {
            string source = await CaptureAsync();
            JToken batch = await _app.CallAsync("jobs.submitBatch", new
            {
                mode = "serial",
                items = Enumerable.Range(0, 3).Select(i => Job(source, "#hold")).ToArray()
            });
            await WaitForJobAsync((string)batch["jobs"]![0]!["id"]!, "running");

            await _app.CallAsync("jobs.cancel", new { groupId = (string)batch["groupId"]! });
            await WaitForJobAsync((string)batch["jobs"]![0]!["id"]!, "cancelled");
            JToken list = await _app.CallAsync("jobs.list");

            Assert.All(list, j => Assert.Equal("cancelled", (string)j["state"]!));
            Assert.Single(_app.Backend.Requests);
        }

        [Fact]
        public async Task SubmitBatch_RejectsEmptyMixedOrUnknownMode()
        {
            string source = await CaptureAsync();

            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("jobs.submitBatch", new { mode = "serial", items = new object[0] }));
            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("jobs.submitBatch", new
            {
                mode = "serial",
                items = new[] { Job(source), Job(source, model: "scripted-b") }
            }));
            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("jobs.submitBatch", new { mode = "zigzag", items = new[] { Job(source) } }));
        }

        // ── files ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task SaveImage_WritesTheBytesWithTheRealExtension()
        {
            string asset = await CaptureAsync();
            _app.Host.SaveDialogResult = Path.Combine(_app.DataDirectory, "my render.jpg");

            JToken saved = await _app.CallAsync("files.saveImage", new { assetId = asset, suggestedName = "AIRender_2026:01/02" });

            // The capture is a PNG, so the file gets a .png extension whatever was typed.
            string expected = Path.Combine(_app.DataDirectory, "my render.jpg.png");
            Assert.Equal(expected, (string)saved["path"]!);
            Assert.Equal(TestImages.Png().Bytes, File.ReadAllBytes(expected));
            Assert.Equal("AIRender_20260102.png", _app.Host.SaveRequests.Single().SuggestedFileName);
            Assert.Equal("png", _app.Host.SaveRequests.Single().Extension);
        }

        [Fact]
        public async Task SaveImage_CancelledDialog_ReturnsNull_AndUnknownAssetIsNotFound()
        {
            string asset = await CaptureAsync();
            _app.Host.SaveDialogResult = null;

            Assert.Equal(JTokenType.Null, (await _app.CallAsync("files.saveImage", new { assetId = asset })).Type);
            Assert.Equal("not_found", await _app.ErrorCodeAsync("files.saveImage", new { assetId = new string('b', 64) }));
        }

        [Fact]
        public async Task SaveMany_SanitizesNamesAndNeverOverwrites()
        {
            string png = await CaptureAsync();
            string jpeg = await _app.UploadAsync(TestImages.Jpeg());
            string folder = Path.Combine(_app.DataDirectory, "export");
            Directory.CreateDirectory(folder);
            _app.Host.FolderDialogResult = folder;

            JToken result = await _app.CallAsync("files.saveMany", new
            {
                items = new[]
                {
                    new { assetId = png, fileName = "View: A/B" },
                    new { assetId = jpeg, fileName = "View AB" },
                    new { assetId = png, fileName = "View AB" },
                    new { assetId = png, fileName = "???" }
                }
            });

            Assert.Equal(4, (int)result["saved"]!);
            Assert.Equal(folder, (string)result["folder"]!);
            Assert.Equal(
                // Names are de-duplicated on the base name, so a PNG and a JPEG never share one either.
                new[] { "View AB-2.jpg", "View AB-3.png", "View AB.png", "image.png" },
                Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public async Task SaveMany_CancelledDialog_ReturnsNull_AndMissingAssetFailsBeforeAsking()
        {
            string png = await CaptureAsync();
            _app.Host.FolderDialogResult = null;

            Assert.Equal(JTokenType.Null, (await _app.CallAsync("files.saveMany", new { items = new[] { new { assetId = png, fileName = "a" } } })).Type);
            Assert.Equal("not_found", await _app.ErrorCodeAsync("files.saveMany", new { items = new[] { new { assetId = new string('c', 64), fileName = "a" } } }));
            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("files.saveMany", new { items = new object[0] }));
        }

        [Fact]
        public async Task PickImages_ImportsSupportedFilesAndSkipsTheRest()
        {
            string notAnImage = Path.Combine(_app.DataDirectory, "notes.txt");
            File.WriteAllText(notAnImage, "hello");
            _app.Host.OpenDialogResult.Add(TestImages.PathOf("jpeg_baseline_80x60.jpg"));
            _app.Host.OpenDialogResult.Add(notAnImage);
            _app.Host.OpenDialogResult.Add(Path.Combine(_app.DataDirectory, "missing.png"));

            JToken assets = await _app.CallAsync("files.pickImages", new { multiple = true });

            JToken only = Assert.Single(assets);
            Assert.Equal("image/jpeg", (string)only["mimeType"]!);
            Assert.Equal(TestImages.Jpeg().Bytes, await _app.DownloadAsync((string)only["assetId"]!));
        }
    }
}
