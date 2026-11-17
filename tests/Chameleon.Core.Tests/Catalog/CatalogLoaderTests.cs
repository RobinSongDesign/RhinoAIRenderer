using System;
using System.Linq;
using Chameleon.Core.Catalog;
using Chameleon.Core.Generation;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Chameleon.Core.Tests.Catalog
{
    public class CatalogLoaderTests
    {
        private static readonly ChameleonCatalog BuiltIn = CatalogLoader.LoadBuiltIn();

        [Fact]
        public void BuiltIn_HasTheTwoLegacyProviders()
        {
            Assert.Equal(new[] { "Gemini", "BltAI" }, BuiltIn.Providers.Select(p => p.Id).ToArray());
            Assert.Equal("BltAI", BuiltIn.DefaultProviderId);

            CatalogProvider gemini = BuiltIn.FindProvider("Gemini")!;
            Assert.Equal("https://generativelanguage.googleapis.com", gemini.BaseUrl);
            Assert.Equal("goog", gemini.AuthType);
            Assert.Equal("gemini", gemini.ApiFormat);
            Assert.Equal("gemini-3.1-flash-image-preview", gemini.DefaultModel);
            Assert.Equal(
                new[] { "gemini-3.1-flash-image-preview", "gemini-3-pro-image-preview", "gemini-2.5-flash-image" },
                gemini.Models.Select(m => m.Id).ToArray());
            Assert.Equal("Google原生", gemini.DisplayName.Get("zh"));

            CatalogProvider blt = BuiltIn.FindProvider("BltAI")!;
            Assert.Equal("https://hk-api.gptbest.vip", blt.BaseUrl);
            Assert.Equal("bearer", blt.AuthType);
            Assert.Equal("柏拉图AI", blt.DisplayName.Get("zh"));
            Assert.Equal(2, blt.Models.Count);
        }

        [Fact]
        public void BuiltIn_KeepsLegacyModelNicknames()
        {
            CatalogProvider gemini = BuiltIn.FindProvider("Gemini")!;

            Assert.Equal("Nano Banana 2", gemini.Models.Single(m => m.Id == "gemini-3.1-flash-image-preview").DisplayName);
            Assert.Equal("Nano Banana Pro", gemini.Models.Single(m => m.Id == "gemini-3-pro-image-preview").DisplayName);
            Assert.Equal("Nano Banana", gemini.Models.Single(m => m.Id == "gemini-2.5-flash-image").DisplayName);
        }

        [Fact]
        public void BuiltIn_KeepsLegacySizesAndRatios()
        {
            Assert.Equal(new[] { "0.5K", "1K", "2K", "4K" }, BuiltIn.ImageSizes.ToArray());
            Assert.Equal("1K", BuiltIn.DefaultImageSize);
            Assert.Equal(new[] { "1:1", "4:3", "3:2", "16:9", "9:16", "21:9" }, BuiltIn.AspectRatios.ToArray());
        }

        [Fact]
        public void BuiltIn_TextsAreVerbatimCopiesOfTheLegacyStrings()
        {
            JObject legacy = JObject.Parse(TestFixtures.ReadText("catalog/legacy_texts.json"));

            Assert.Equal((string)legacy["systemPrompt"]!, BuiltIn.DefaultSystemPrompt);

            var expected = (JObject)legacy["stylePrompts"]!;
            Assert.Equal(11, BuiltIn.StyleTemplates.Count);
            Assert.Equal(expected.Properties().Select(p => p.Name).ToArray(), BuiltIn.StyleTemplates.Select(t => t.Id).ToArray());
            foreach (StyleTemplate template in BuiltIn.StyleTemplates)
            {
                Assert.Equal((string)expected[template.Id]!, template.Prompt);
                Assert.False(string.IsNullOrEmpty(template.Name.Zh));
                Assert.False(string.IsNullOrEmpty(template.Name.En));
            }

            Assert.Equal(string.Empty, BuiltIn.StyleTemplates.Single(t => t.Id == StyleTemplate.CustomId).Prompt);
        }

        [Fact]
        public void ResolveModel_KnownModelKeepsNameAndCapabilitiesAcrossProvidersOfSameFormat()
        {
            ModelInfo model = BuiltIn.ResolveModel("gemini", "gemini-3-pro-image-preview");

            Assert.Equal("Nano Banana Pro", model.DisplayName);
            Assert.Equal(8, model.Capabilities.MaxInputImages);
            Assert.False(model.Capabilities.SupportsMask);
        }

        [Fact]
        public void ResolveModel_UnknownModelGetsFormatDefaults()
        {
            ModelInfo gemini = BuiltIn.ResolveModel("gemini", "some-new-model");
            ModelInfo openai = BuiltIn.ResolveModel("openai", "gpt-image-2");

            Assert.Equal("some-new-model", gemini.DisplayName);
            Assert.Equal(4, gemini.Capabilities.MaxInputImages);
            Assert.True(openai.Capabilities.SupportsMask);
        }

        [Fact]
        public void ResolveModel_ReturnsIndependentCapabilityCopies()
        {
            ModelInfo first = BuiltIn.ResolveModel("gemini", "gemini-3-pro-image-preview");
            first.Capabilities.MaxInputImages = 99;

            Assert.Equal(8, BuiltIn.ResolveModel("gemini", "gemini-3-pro-image-preview").Capabilities.MaxInputImages);
        }

        [Fact]
        public void GetDefaultCapabilities_UnknownFormatIsEmpty()
        {
            BackendCapabilities caps = BuiltIn.GetDefaultCapabilities("unknown");

            Assert.Equal(0, caps.MaxInputImages);
            Assert.Empty(caps.ImageSizes);
        }

        [Theory]
        [InlineData("{ not json", "not valid JSON")]
        [InlineData("null", "empty")]
        [InlineData("{\"providers\":[{\"id\":\"A\",\"baseUrl\":\"u\",\"apiFormat\":\"gemini\",\"defaultModel\":\"m\",\"models\":[{\"id\":\"m\"}]},{\"id\":\"A\",\"baseUrl\":\"u\",\"apiFormat\":\"gemini\",\"defaultModel\":\"m\",\"models\":[{\"id\":\"m\"}]}],\"defaultProviderId\":\"A\"}", "Duplicate provider id")]
        [InlineData("{\"providers\":[{\"id\":\"A\",\"baseUrl\":\"u\",\"apiFormat\":\"gemini\",\"defaultModel\":\"x\",\"models\":[{\"id\":\"m\"}]}],\"defaultProviderId\":\"A\"}", "Default model")]
        [InlineData("{\"providers\":[{\"id\":\"A\",\"baseUrl\":\"u\",\"apiFormat\":\"gemini\",\"defaultModel\":\"m\",\"models\":[{\"id\":\"m\"}]}],\"defaultProviderId\":\"B\"}", "default provider")]
        [InlineData("{\"providers\":[{\"id\":\"A\",\"baseUrl\":\"u\",\"apiFormat\":\"gemini\",\"defaultModel\":\"m\",\"models\":[]}],\"defaultProviderId\":\"A\"}", "no models")]
        [InlineData("{\"imageSizes\":[\"1K\"],\"defaultImageSize\":\"2K\"}", "default image size")]
        public void Parse_RejectsInvalidCatalogs(string json, string expectedFragment)
        {
            FormatException ex = Assert.Throws<FormatException>(() => CatalogLoader.Parse(json));
            Assert.Contains(expectedFragment, ex.Message);
        }

        [Fact]
        public void LocalizedText_FallsBackWhenTranslationMissing()
        {
            Assert.Equal("只有中文", new LocalizedText("只有中文", "").Get("en"));
            Assert.Equal("only english", new LocalizedText("", "only english").Get("zh-CN"));
            Assert.Equal("EN", new LocalizedText("ZH", "EN").Get("fr"));
        }
    }
}
