using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Catalog;
using Chameleon.Core.Generation;
using Chameleon.Core.Providers;
using Chameleon.Core.Settings;
using Xunit;

namespace Chameleon.Core.Tests.Providers
{
    public class ModelListingTests
    {
        private static Task<IReadOnlyList<string>> ParseAsync(IProviderAdapter adapter, ProviderProfile profile, string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            return adapter.ParseListModelsResponseAsync(AdapterTestKit.Context(profile), AdapterTestKit.JsonResponse(status, json), CancellationToken.None);
        }

        [Fact]
        public void Gemini_ListsModelsWithTheSameAuthenticationAsGenerations()
        {
            var adapter = new GeminiAdapter();

            var goog = adapter.BuildListModelsRequest(AdapterTestKit.Context(AdapterTestKit.GeminiProfile()));
            var bearer = adapter.BuildListModelsRequest(AdapterTestKit.Context(AdapterTestKit.GeminiProfile(AuthTypes.Bearer)));

            Assert.Equal("https://gemini.test/v1beta/models?pageSize=1000", goog.RequestUri!.ToString());
            Assert.Equal(AdapterTestKit.ApiKey, goog.Headers.GetValues("x-goog-api-key").Single());
            Assert.Equal("Bearer " + AdapterTestKit.ApiKey, bearer.Headers.Authorization!.ToString());
        }

        [Fact]
        public async Task Gemini_KeepsOnlyImageModelsThatGenerateContent_FromGoogle()
        {
            const string google = @"{ ""models"": [
              { ""name"": ""models/gemini-4-carbon"", ""supportedGenerationMethods"": [""generateContent""] },
              { ""name"": ""models/gemini-nano-banana-2.1"", ""supportedGenerationMethods"": [""generateContent"", ""countTokens""] },
              { ""name"": ""models/gemini-3-pro-image"", ""supportedGenerationMethods"": [""generateContent""] },
              { ""name"": ""models/imagen-4.0-generate-001"", ""supportedGenerationMethods"": [""predict""] },
              { ""name"": ""models/gemini-3.1-flash-image"", ""supportedGenerationMethods"": [""batchGenerateContent""] },
              { ""name"": ""models/gemini-3-pro-image"", ""supportedGenerationMethods"": [""generateContent""] }
            ], ""nextPageToken"": """" }";

            IReadOnlyList<string> ids = await ParseAsync(new GeminiAdapter(), AdapterTestKit.GeminiProfile(), google);

            Assert.Equal(new[] { "gemini-nano-banana-2.1", "gemini-3-pro-image" }, ids);
        }

        [Fact]
        public async Task Gemini_ReadsTheBareNamesRelaysUse()
        {
            // new-api's /v1beta/models: bare names, no supportedGenerationMethods.
            const string relay = @"{ ""models"": [ { ""name"": ""gemini-nano-banana-2.1"", ""displayName"": ""gemini-nano-banana-2.1"" }, { ""name"": ""deepseek-v4"" } ] }";

            IReadOnlyList<string> ids = await ParseAsync(new GeminiAdapter(), AdapterTestKit.GeminiProfile(), relay);

            Assert.Equal(new[] { "gemini-nano-banana-2.1" }, ids);
        }

        [Fact]
        public async Task Listings_SkipEntriesThatAreNotObjects()
        {
            IReadOnlyList<string> gemini = await ParseAsync(new GeminiAdapter(), AdapterTestKit.GeminiProfile(), @"{ ""models"": [ ""x"", 3, { ""name"": ""gemini-nano-banana-2.1"" } ] }");
            IReadOnlyList<string> openai = await ParseAsync(new OpenAIImagesAdapter(), AdapterTestKit.OpenAIProfile(), @"{ ""data"": [ null, { ""id"": ""gpt-image-2"" } ] }");

            Assert.Equal(new[] { "gemini-nano-banana-2.1" }, gemini);
            Assert.Equal(new[] { "gpt-image-2" }, openai);
        }

        [Fact]
        public async Task OpenAI_KeepsOnlyImagesApiModels()
        {
            const string json = @"{ ""object"": ""list"", ""data"": [ { ""id"": ""gpt-5"" }, { ""id"": ""gpt-image-2"" }, { ""id"": ""dall-e-3"" }, { ""id"": ""gpt-image-2"" } ] }";

            IReadOnlyList<string> ids = await ParseAsync(new OpenAIImagesAdapter(), AdapterTestKit.OpenAIProfile(), json);

            Assert.Equal(new[] { "gpt-image-2", "dall-e-3" }, ids);
        }

        [Fact]
        public async Task Listings_ThatAreErrorsOrMalformed_Throw()
        {
            RenderException rejected = await Assert.ThrowsAsync<RenderException>(() =>
                ParseAsync(new GeminiAdapter(), AdapterTestKit.GeminiProfile(), @"{""error"":{""status"":""UNAUTHENTICATED"",""message"":""bad key""}}", HttpStatusCode.Unauthorized));
            RenderException malformed = await Assert.ThrowsAsync<RenderException>(() =>
                ParseAsync(new OpenAIImagesAdapter(), AdapterTestKit.OpenAIProfile(), @"{""models"":[]}"));

            Assert.Equal(RenderErrorCode.AuthFailed, rejected.Code);
            Assert.Equal(RenderErrorCode.ProviderError, malformed.Code);
        }

        [Fact]
        public void Registry_PrefersTheUsersListThenTheDiscoveredOneThenTheCatalog()
        {
            var registry = new ProviderRegistry(CatalogLoader.LoadBuiltIn());
            var discovered = new Dictionary<string, DiscoveredModels>
            {
                ["Chameleon"] = new DiscoveredModels { Models = new List<string> { "gemini-3-pro-image", "gemini-nano-banana-3" } },
                ["Gemini"] = new DiscoveredModels { Models = new List<string> { "gemini-3-pro-image" } }
            };
            var overrides = new Dictionary<string, ProviderOverride>
            {
                ["Gemini"] = new ProviderOverride { Models = new List<string> { "my-pick" } }
            };

            IReadOnlyList<ProviderProfile> providers = registry.Build(null, overrides, "en", discovered);
            ProviderProfile account = providers.Single(p => p.Id == "Chameleon");
            ProviderProfile gemini = providers.Single(p => p.Id == "Gemini");

            Assert.Equal(new[] { "gemini-3-pro-image", "gemini-nano-banana-3" }, account.Models.Select(m => m.Id));
            // The catalog default is gone from the endpoint: the first offered model takes over.
            Assert.Equal("gemini-3-pro-image", account.DefaultModel);
            // Known models keep the catalog's capabilities; unknown ones get the format's defaults.
            Assert.Equal(14, account.FindModel("gemini-3-pro-image")!.Capabilities.MaxInputImages);
            Assert.Equal("gemini-nano-banana-3", account.FindModel("gemini-nano-banana-3")!.DisplayName);
            Assert.Equal(new[] { "my-pick" }, gemini.Models.Select(m => m.Id));
        }
    }
}
