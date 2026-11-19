using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Chameleon.App.Hosting;
using Chameleon.App.Platform;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Chameleon.App.Tests.Services
{
    public class AppAndProviderServiceTests : IDisposable
    {
        private readonly TestApp _app = new TestApp();

        public void Dispose() => _app.Dispose();

        // ── app.getState / settings ────────────────────────────────────────────

        [Fact]
        public async Task GetState_DescribesHostBackendsCatalogAndSettings()
        {
            JToken state = await _app.CallAsync("app.getState");

            Assert.Equal(1, (int)state["protocolVersion"]!);
            Assert.Equal("2.0.0-test", (string)state["appVersion"]!);
            Assert.Equal("dev", (string)state["platform"]!);
            Assert.Equal("8.36", (string)state["rhinoVersion"]!);
            Assert.EndsWith("logs", (string)state["logsDirectory"]!);
            Assert.Equal("direct:Chameleon", (string)state["activeBackendId"]!);
            Assert.Equal("gemini-nano-banana-2.1", (string)state["activeModelId"]!);
            Assert.Equal(new[] { "direct:Chameleon", "direct:Gemini", "scripted" }, state["backends"]!.Select(b => (string)b["id"]!).ToArray());
            Assert.Equal(new[] { false, false, true }, state["backends"]!.Select(b => (bool)b["ready"]!).ToArray());
            Assert.Equal("dev", (string)state["backends"]![2]!["kind"]!);
            Assert.Equal("Nano Banana Pro", (string)state["backends"]![1]!["models"]![2]!["displayName"]!);
            Assert.Equal(14, (int)state["backends"]![1]!["models"]![2]!["capabilities"]!["maxInputImages"]!);
            // The Chameleon account reports its balance and links to its top-up page.
            Assert.True((bool)state["backends"]![0]!["supportsBalance"]!);
            Assert.StartsWith("https://", (string)state["backends"]![0]!["topUpUrl"]!);
            Assert.Equal(11, ((JArray)state["catalog"]!["styleTemplates"]!).Count);
            Assert.False(string.IsNullOrEmpty((string)state["catalog"]!["defaultSystemPrompt"]!));
            Assert.Equal(JTokenType.Null, state["settings"]!["systemPrompt"]!.Type);
            Assert.Equal("serial", (string)state["settings"]!["batch"]!["mode"]!);
        }

        [Fact]
        public async Task SettingsUpdate_ChangesOnlyTheGivenFields_AndNotifies()
        {
            JToken afterPrompt = await _app.CallAsync("settings.update", new { systemPrompt = "be precise" });
            JToken changed = await _app.WaitForEventAsync("settings.changed");
            JToken afterBatch = await _app.CallAsync("settings.update", new { batch = new { mode = "anchorParallel", maxConsistencyRefs = 99 } });
            JToken afterBogus = await _app.CallAsync("settings.update", new { batch = new { mode = "nonsense" }, language = "zh-CN" });
            JToken afterReset = await _app.CallAsync("settings.update", JObject.Parse("{\"systemPrompt\":null}"));

            Assert.Equal("be precise", (string)afterPrompt["systemPrompt"]!);
            Assert.Equal("be precise", (string)changed["systemPrompt"]!);
            Assert.Equal("anchorParallel", (string)afterBatch["batch"]!["mode"]!);
            Assert.Equal(16, (int)afterBatch["batch"]!["maxConsistencyRefs"]!);
            Assert.Equal("be precise", (string)afterBatch["systemPrompt"]!);
            Assert.Equal("anchorParallel", (string)afterBogus["batch"]!["mode"]!);
            Assert.Equal("zh", (string)afterBogus["language"]!);
            Assert.Equal(JTokenType.Null, afterReset["systemPrompt"]!.Type);
        }

        [Fact]
        public async Task LanguageChange_RelocalizesBuiltInProviderNames()
        {
            await _app.CallAsync("settings.update", new { language = "zh" });

            JToken state = await _app.CallAsync("app.getState");

            Assert.Equal("Chameleon 账号", (string)state["backends"]![0]!["displayName"]!);
            Assert.Equal("Google原生", (string)state["backends"]![1]!["displayName"]!);
        }

        [Fact]
        public async Task SettingsUpdate_WithoutAnObject_IsInvalidRequest()
        {
            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("settings.update"));
        }

        // ── backend.select ─────────────────────────────────────────────────────

        [Fact]
        public async Task BackendSelect_WhileAnotherAnnouncementIsResolving_AnnouncesTheNewestSelectionLast()
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task held = _app.Backend.HoldNextModelList(release.Task);
            Task<JToken> first = _app.CallAsync("backend.select", new { backendId = "scripted" });
            await held;

            // The second selection must not overtake the first, still waiting for the model list.
            Task<JToken> second = _app.CallAsync("backend.select", new { backendId = "direct:Gemini" });
            await Task.Delay(200);
            release.SetResult(true);
            await Task.WhenAll(first, second);

            JToken announcedFirst = await _app.WaitForEventAsync("backends.changed");
            JToken announcedLast = await _app.WaitForEventAsync("backends.changed");
            Assert.Equal("scripted", (string)announcedFirst["activeBackendId"]!);
            Assert.Equal("direct:Gemini", (string)announcedLast["activeBackendId"]!);
            Assert.Equal("direct:Gemini", (string)(await _app.CallAsync("app.getState"))["activeBackendId"]!);
        }

        [Fact]
        public async Task BackendSelect_PersistsBackendAndModel_AndNotifies()
        {
            JToken selection = await _app.CallAsync("backend.select", new { backendId = "scripted", modelId = "scripted-b" });
            JToken announced = await _app.WaitForEventAsync("backends.changed");
            JToken state = await _app.CallAsync("app.getState");

            Assert.Equal("scripted", (string)selection["activeBackendId"]!);
            Assert.Equal("scripted-b", (string)selection["activeModelId"]!);
            Assert.Equal("scripted", (string)announced["activeBackendId"]!);
            Assert.Equal("scripted-b", (string)state["activeModelId"]!);
        }

        [Fact]
        public async Task BackendSelect_RemembersTheModelPerBackend_AndIgnoresModelsThatNoLongerExist()
        {
            await _app.CallAsync("backend.select", new { backendId = "scripted", modelId = "scripted-b" });
            await _app.CallAsync("backend.select", new { backendId = "direct:Gemini", modelId = "retired-model" });
            JToken gemini = await _app.CallAsync("app.getState");
            JToken backToScripted = await _app.CallAsync("backend.select", new { backendId = "scripted" });

            Assert.Equal("gemini-nano-banana-2.1", (string)gemini["activeModelId"]!);
            Assert.Equal("scripted-b", (string)backToScripted["activeModelId"]!);
        }

        [Fact]
        public async Task BackendSelect_UnknownBackend_IsNotFound()
        {
            Assert.Equal("not_found", await _app.ErrorCodeAsync("backend.select", new { backendId = "direct:nope" }));
        }

        // ── providers ──────────────────────────────────────────────────────────

        [Fact]
        public async Task ProvidersList_ReportsBuiltInsWithoutKeys()
        {
            JToken providers = await _app.CallAsync("providers.list");

            Assert.Equal(new[] { "Chameleon", "Gemini" }, providers.Select(p => (string)p["id"]!).ToArray());
            Assert.All(providers, p => Assert.True((bool)p["isBuiltIn"]!));
            Assert.All(providers, p => Assert.False((bool)p["hasKey"]!));
            Assert.Equal("https://aistudio.google.com/app/apikey", (string)providers[1]!["apiKeyUrl"]!);
        }

        [Fact]
        public async Task ProvidersSave_CreatesAUserProviderThatBecomesABackend()
        {
            JToken saved = await _app.CallAsync("providers.save", new
            {
                displayName = " My Relay ",
                baseUrl = "https://relay.example.com/",
                apiFormat = "openai",
                authType = "goog",
                models = new[] { " gpt-image-2 ", "", "gpt-image-2", "other" }
            });
            JToken state = await _app.CallAsync("app.getState");

            string id = (string)saved["id"]!;
            Assert.Equal(12, id.Length);
            Assert.Equal("My Relay", (string)saved["displayName"]!);
            Assert.Equal("https://relay.example.com", (string)saved["baseUrl"]!);
            Assert.Equal("bearer", (string)saved["authType"]!); // OpenAI format only knows bearer
            Assert.Equal(new[] { "gpt-image-2", "other" }, saved["models"]!.Select(m => (string)m!).ToArray());
            Assert.Equal("gpt-image-2", (string)saved["defaultModel"]!);
            Assert.False((bool)saved["isBuiltIn"]!);
            Assert.Contains("direct:" + id, state["backends"]!.Select(b => (string)b["id"]!));
        }

        [Fact]
        public async Task ProvidersSave_UpdatesAnExistingUserProvider()
        {
            string id = (string)(await _app.CallAsync("providers.save", new { displayName = "A", baseUrl = "https://a.example.com", models = new[] { "m1" } }))["id"]!;

            await _app.CallAsync("providers.save", new { id, displayName = "B", baseUrl = "https://b.example.com", models = new[] { "m1", "m2" }, defaultModel = "m2" });
            JToken providers = await _app.CallAsync("providers.list");

            JToken updated = providers.Single(p => (string)p["id"]! == id);
            Assert.Equal(3, providers.Count());
            Assert.Equal("B", (string)updated["displayName"]!);
            Assert.Equal("m2", (string)updated["defaultModel"]!);
        }

        [Theory]
        [InlineData("", "https://x.example.com", "m", "gemini")]
        [InlineData("Name", "ftp://x.example.com", "m", "gemini")]
        [InlineData("Name", "not a url", "m", "gemini")]
        [InlineData("Name", "https://x.example.com", "", "gemini")]
        [InlineData("Name", "https://x.example.com", "m", "smoke-signals")]
        public async Task ProvidersSave_RejectsInvalidInput(string name, string url, string model, string format)
        {
            string code = await _app.ErrorCodeAsync("providers.save", new { displayName = name, baseUrl = url, apiFormat = format, models = new[] { model } });

            Assert.Equal("invalid_request", code);
        }

        [Fact]
        public async Task ProvidersSave_OnABuiltIn_StoresAnOverride_AndResetRestoresIt()
        {
            JToken overridden = await _app.CallAsync("providers.save", new
            {
                id = "Chameleon",
                displayName = "Account mirror",
                baseUrl = "https://mirror.example.com",
                apiFormat = "gemini",
                authType = "bearer",
                models = new[] { "gemini-3-pro-image" },
                enableSearchGrounding = false
            });
            JToken reset = await _app.CallAsync("providers.resetBuiltIn", new { id = "Chameleon" });

            Assert.True((bool)overridden["isBuiltIn"]!);
            Assert.True((bool)overridden["isOverridden"]!);
            Assert.Equal("https://mirror.example.com", (string)overridden["baseUrl"]!);
            Assert.False((bool)overridden["enableSearchGrounding"]!);
            Assert.False((bool)reset["isOverridden"]!);
            Assert.Equal("https://api.robinsong.top", (string)reset["baseUrl"]!);
            Assert.Equal(4, ((JArray)reset["models"]!).Count);
            Assert.Equal("not_found", await _app.ErrorCodeAsync("providers.resetBuiltIn", new { id = "nope" }));
        }

        [Fact]
        public async Task ProvidersDelete_RemovesProviderAndKey_AndFallsBackWhenItWasActive()
        {
            string id = (string)(await _app.CallAsync("providers.save", new { displayName = "Temp", baseUrl = "https://t.example.com", models = new[] { "m" } }))["id"]!;
            await _app.CallAsync("providers.setKey", new { id, key = "temp-secret-key" });
            await _app.CallAsync("backend.select", new { backendId = "direct:" + id });

            await _app.CallAsync("providers.delete", new { id });
            JToken state = await _app.CallAsync("app.getState");

            Assert.False(_app.Secrets.Contains("provider:" + id));
            Assert.Equal("direct:Chameleon", (string)state["activeBackendId"]!);
            Assert.DoesNotContain("direct:" + id, state["backends"]!.Select(b => (string)b["id"]!));
            Assert.Equal("not_found", await _app.ErrorCodeAsync("providers.delete", new { id }));
        }

        [Fact]
        public async Task ProvidersDelete_BuiltIn_IsRefused()
        {
            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("providers.delete", new { id = "Gemini" }));
        }

        [Fact]
        public async Task SetKey_MakesTheBackendReady_AndClearingItMakesItUnready()
        {
            JToken set = await _app.CallAsync("providers.setKey", new { id = "Chameleon", key = "  " + TestApp.GoodKey + " " });
            JToken announced = await _app.WaitForEventAsync("backends.changed");
            JToken cleared = await _app.CallAsync("providers.setKey", new { id = "Chameleon", key = "" });

            Assert.True((bool)set["hasKey"]!);
            Assert.True((bool)announced["backends"]!.Single(b => (string)b["id"]! == "direct:Chameleon")["ready"]!);
            Assert.False((bool)cleared["hasKey"]!);
            Assert.False(_app.Secrets.Contains("provider:Chameleon"));
            Assert.Equal("not_found", await _app.ErrorCodeAsync("providers.setKey", new { id = "nope", key = "x" }));
        }

        [Fact]
        public async Task TestKey_WithAGivenKey_ChecksItWithoutStoringIt()
        {
            JToken ok = await _app.CallAsync("providers.testKey", new { id = "Chameleon", key = TestApp.GoodKey });
            string rejected = await _app.ErrorCodeAsync("providers.testKey", new { id = "Chameleon", key = "wrong-key-value" });

            Assert.True((bool)ok["ok"]!);
            Assert.Equal("auth_failed", rejected);
            Assert.False(_app.Secrets.Contains("provider:Chameleon"));
            Assert.Equal(2, _app.ProviderRequests.Count);
            Assert.EndsWith(":generateContent", _app.ProviderRequests[0].RequestUri!.AbsolutePath);
        }

        [Fact]
        public async Task TestKey_WithoutAKey_UsesTheStoredOne()
        {
            Assert.Equal("auth_failed", await _app.ErrorCodeAsync("providers.testKey", new { id = "Gemini" }));

            await _app.CallAsync("providers.setKey", new { id = "Gemini", key = TestApp.GoodKey });
            JToken ok = await _app.CallAsync("providers.testKey", new { id = "Gemini", modelId = "gemini-3-pro-image" });

            Assert.True((bool)ok["ok"]!);
            Assert.Contains("gemini-3-pro-image", _app.ProviderRequests.Last().RequestUri!.AbsolutePath);
        }

        [Fact]
        public async Task StoredKeys_NeverAppearInAnyResponseOrEventOrSettingsFile()
        {
            await _app.CallAsync("providers.setKey", new { id = "Chameleon", key = TestApp.GoodKey });
            JToken announced = await _app.WaitForEventAsync("backends.changed");
            await _app.CallAsync("providers.list");
            await _app.CallAsync("providers.testKey", new { id = "Chameleon" });
            await _app.CallAsync("app.getState");
            await _app.CallAsync("backend.select", new { backendId = "direct:Chameleon" });
            await _app.CallAsync("providers.save", new { id = "Chameleon", displayName = "x", baseUrl = "https://x.example.com", models = new[] { "m" } });

            Assert.DoesNotContain(TestApp.GoodKey, _app.AllResponses.ToString());
            Assert.DoesNotContain(TestApp.GoodKey, announced.ToString());
            Assert.DoesNotContain(TestApp.GoodKey, File.ReadAllText(Path.Combine(_app.DataDirectory, "settings.json")));
            Assert.DoesNotContain(TestApp.GoodKey, string.Join("\n", _app.Host.LogLines));
        }

        // ── shell ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task OpenUrl_OnlyAcceptsHttps()
        {
            await _app.CallAsync("shell.openUrl", new { url = "https://aistudio.google.com/app/apikey" });

            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("shell.openUrl", new { url = "http://example.com" }));
            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("shell.openUrl", new { url = "file:///etc/passwd" }));
            Assert.Equal("invalid_request", await _app.ErrorCodeAsync("shell.openUrl", new { url = "calc.exe" }));
            Assert.Equal(new[] { "https://aistudio.google.com/app/apikey" }, _app.Host.OpenedUrls.ToArray());
        }

        [Fact]
        public async Task LogWrite_ForwardsToTheHostLog()
        {
            await _app.CallAsync("log.write", new { level = "warn", message = "something odd" });

            Assert.Contains("[ui:warn] something odd", _app.Host.LogLines);
        }
    }

    public class LegacyMigrationOnStartTests
    {
        private sealed class PathsWithLegacy : IAppPaths
        {
            private readonly AppPaths _inner;

            public PathsWithLegacy(string root)
            {
                _inner = AppPaths.UnderRoot(root);
                LegacySettingsFile = Path.Combine(root, "legacy", "settings.json");
            }

            public string SettingsFile => _inner.SettingsFile;

            public string ProjectsDirectory => _inner.ProjectsDirectory;

            public string CacheDirectory => _inner.CacheDirectory;

            public string LogsDirectory => _inner.LogsDirectory;

            public string SecretsDirectory => _inner.SecretsDirectory;

            public string? LegacySettingsFile { get; }
        }

        [Fact]
        public async Task Start_ImportsTheWpfVersionsSettingsOnce()
        {
            using var root = new TempDirectory();
            var paths = new PathsWithLegacy(root.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.LegacySettingsFile!)!);
            File.WriteAllText(paths.LegacySettingsFile!, "{\"ApiKeys\":{\"Gemini\":\"" + TestApp.GoodKey + "\"},\"SelectedProvider\":0,\"SelectedModel\":\"gemini-3-pro-image\",\"LanguageIndex\":0}");

            using var app = new TestApp(paths: paths);
            JToken state = await app.CallAsync("app.getState");

            Assert.Equal("direct:Gemini", (string)state["activeBackendId"]!);
            Assert.Equal("gemini-3-pro-image", (string)state["activeModelId"]!);
            Assert.Equal("zh", (string)state["language"]!);
            Assert.True((bool)state["backends"]!.Single(b => (string)b["id"]! == "direct:Gemini")["ready"]!);
            Assert.Equal(TestApp.GoodKey, app.Secrets.Get("provider:Gemini"));
            Assert.False(File.Exists(paths.LegacySettingsFile));
            Assert.Contains(app.Host.LogLines, l => l.Contains("Imported settings"));
        }
    }
}
