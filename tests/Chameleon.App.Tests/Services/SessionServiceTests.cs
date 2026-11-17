using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Chameleon.App.Tests.Services
{
    public class SessionServiceTests : IDisposable
    {
        private readonly TestApp _app = new TestApp();

        public void Dispose() => _app.Dispose();

        private async Task<string> CaptureAsync()
        {
            return (string)(await _app.CallAsync("rhino.captureActiveView"))["assetId"]!;
        }

        private async Task<JToken> AddCaptureAsync()
        {
            return await _app.CallAsync("session.addNode", new { kind = "capture", assetId = await CaptureAsync(), label = "Perspective" });
        }

        private Task<JToken> FinishedAsync(string jobId)
        {
            return _app.WaitForEventAsync("job.updated", j => (string)j["id"]! == jobId && (string)j["state"]! == "succeeded");
        }

        [Fact]
        public async Task AddNode_DescribesTheNodeAndAnnouncesTheSession()
        {
            JToken node = await AddCaptureAsync();
            JToken announced = await _app.WaitForEventAsync("session.changed");

            Assert.Equal("capture", (string)node["kind"]!);
            Assert.Equal("Perspective", (string)node["label"]!);
            Assert.Equal(64, (int)node["asset"]!["width"]!);
            Assert.Equal((string)node["id"]!, (string)announced["nodes"]![0]!["id"]!);
            Assert.StartsWith("unsaved-", (string)announced["projectId"]!);
        }

        [Fact]
        public async Task AddNode_RejectsUnknownImagesAndKinds()
        {
            Assert.Equal("not_found", await _app.ErrorCodeAsync("session.addNode", new { kind = "capture", assetId = new string('a', 64) }));
            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("session.addNode", new { kind = "sketch", assetId = await CaptureAsync() }));
            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("session.addNode", new { kind = "capture", assetId = "x" }));
        }

        [Fact]
        public async Task SucceededJobs_BecomeChildrenOfTheirParentNode()
        {
            JToken capture = await AddCaptureAsync();
            string captureId = (string)capture["id"]!;

            JToken job = await _app.CallAsync("jobs.submit", new
            {
                backendId = "scripted", modelId = "scripted-a", prompt = "dusk", imageSize = "2K",
                sourceAssetId = (string)capture["asset"]!["assetId"]!, parentNodeId = captureId
            });
            JToken done = await FinishedAsync((string)job["id"]!);

            string nodeId = (string)done["result"]!["nodeIds"]![0]!;
            JToken session = await _app.CallAsync("session.get");
            JToken node = session["nodes"]!.Single(n => (string)n["id"]! == nodeId);
            Assert.Equal(captureId, (string)node["parentId"]!);
            Assert.Equal("generation", (string)node["kind"]!);
            Assert.Equal("dusk", (string)node["recipe"]!["prompt"]!);
            Assert.Equal("2K", (string)node["recipe"]!["imageSize"]!);
            Assert.Equal("scripted", (string)node["recipe"]!["backendId"]!);
            Assert.Equal((string)done["result"]!["assets"]![0]!["assetId"]!, (string)node["asset"]!["assetId"]!);

            // jobs.list reports the same nodes.
            JToken listed = (await _app.CallAsync("jobs.list")).Single(j => (string)j["id"]! == (string)job["id"]!);
            Assert.Equal(nodeId, (string)listed["result"]!["nodeIds"]![0]!);
        }

        [Fact]
        public async Task DeferredJobs_LeaveTheNodeToTheUi()
        {
            JToken job = await _app.CallAsync("jobs.submit", new { backendId = "scripted", modelId = "scripted-a", prompt = "p", imageSize = "1K", sourceAssetId = await CaptureAsync(), deferNode = true });
            JToken done = await FinishedAsync((string)job["id"]!);

            Assert.Null(done["result"]!["nodeIds"]);
            Assert.Empty((await _app.CallAsync("session.get"))["nodes"]!);
        }

        [Fact]
        public async Task UpdateSelectAndDelete_WorkThroughTheBridge()
        {
            JToken root = await AddCaptureAsync();
            JToken child = await _app.CallAsync("session.addNode", new { kind = "import", assetId = (string)root["asset"]!["assetId"]!, parentId = (string)root["id"]! });

            JToken updated = await _app.CallAsync("session.updateNode", new { id = (string)child["id"]!, label = "Mine", starred = true });
            await _app.CallAsync("session.setCurrent", new { id = (string)child["id"]! });
            Assert.Equal("Mine", (string)updated["label"]!);
            Assert.True((bool)updated["starred"]!);
            Assert.Equal((string)child["id"]!, (string)(await _app.CallAsync("session.get"))["currentNodeId"]!);

            await _app.CallAsync("session.deleteNode", new { id = (string)root["id"]!, withDescendants = true });
            JToken session = await _app.CallAsync("session.get");
            Assert.Empty(session["nodes"]!);
            Assert.Null(session["currentNodeId"]);

            Assert.Equal("not_found", await _app.ErrorCodeAsync("session.setCurrent", new { id = "nope" }));
        }

        [Fact]
        public async Task ProjectImages_StayDownloadableFromTheBridge()
        {
            JToken node = await AddCaptureAsync();

            byte[] bytes = await _app.DownloadAsync((string)node["asset"]!["assetId"]!);

            Assert.Equal(TestImages.Png().Bytes, bytes);
        }
    }
}
