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

        private static readonly string[] CurrentGoogleImageModels =
        {
            "gemini-nano-banana-2.1", "gemini-3.1-flash-image", "gemini-3-pro-image", "gemini-3.1-flash-lite-image"
        };

        [Fact]
        public void BuiltIn_OffersTheChameleonAccountFirstAndGoogleDirect()
        {
            Assert.Equal(new[] { "Chameleon", "Gemini" }, BuiltIn.Providers.Select(p => p.Id).ToArray());
            Assert.Equal("Chameleon", BuiltIn.DefaultProviderId);

            CatalogProvider account = BuiltIn.FindProvider("Chameleon")!;
            Assert.Equal("https://api.robinsong.top", account.BaseUrl);
            Assert.Equal("bearer", account.AuthType);
            Assert.Equal("gemini", account.ApiFormat);
            Assert.Equal("oneapi", account.BalanceApi);
            Assert.StartsWith("https://", account.TopUpUrl);
            Assert.Equal("Chameleon 账号", account.DisplayName.Get("zh"));

            CatalogProvider gemini = BuiltIn.FindProvider("Gemini")!;
            Assert.Equal("https://generativelanguage.googleapis.com", gemini.BaseUrl);
            Assert.Equal("goog", gemini.AuthType);
            Assert.Equal("gemini", gemini.ApiFormat);
            Assert.Null(gemini.BalanceApi);
            Assert.Equal("Google原生", gemini.DisplayName.Get("zh"));
        }

        [Fact]
        public void BuiltIn_OffersGooglesCurrentImageModels_NotTheRetiredPreviews()
        {
            foreach (CatalogProvider provider in BuiltIn.Providers)
            {
                Assert.Equal("gemini-nano-banana-2.1", provider.DefaultModel);
                Assert.Equal(CurrentGoogleImageModels, provider.Models.Select(m => m.Id).ToArray());
                // Shut down by Google on 2026-06-25.
                Assert.DoesNotContain(provider.Models, m => m.Id.EndsWith("-preview", System.StringComparison.Ordinal));
            }

            CatalogProvider gemini = BuiltIn.FindProvider("Gemini")!;
            Assert.Equal("Nano Banana 2.1", gemini.Models.Single(m => m.Id == "gemini-nano-banana-2.1").DisplayName);
            Assert.Equal("Nano Banana Pro", gemini.Models.Single(m => m.Id == "gemini-3-pro-image").DisplayName);
        }

        [Fact]
        public void BuiltIn_ModelCapabilitiesFollowGooglesDocumentation()
        {
            ModelInfo nanoBanana21 = BuiltIn.ResolveModel("gemini", "gemini-nano-banana-2.1");
            ModelInfo flash = BuiltIn.ResolveModel("gemini", "gemini-3.1-flash-image");
            ModelInfo pro = BuiltIn.ResolveModel("gemini", "gemini-3-pro-image");
            ModelInfo lite = BuiltIn.ResolveModel("gemini", "gemini-3.1-flash-lite-image");

            Assert.Equal(new[] { "1K", "2K", "4K" }, nanoBanana21.Capabilities.ImageSizes.ToArray());
            Assert.Equal(new[] { "0.5K", "1K", "2K", "4K" }, flash.Capabilities.ImageSizes.ToArray());
            Assert.Equal(new[] { "1K" }, lite.Capabilities.ImageSizes.ToArray());
            Assert.Equal(14, nanoBanana21.Capabilities.MaxInputImages);
            Assert.Contains("8:1", nanoBanana21.Capabilities.AspectRatios);
            Assert.DoesNotContain("8:1", pro.Capabilities.AspectRatios);
        }

        [Fact]
        public void BuiltIn_SizesAndRatiosCoverEveryModel()
        {
            Assert.Equal(new[] { "0.5K", "1K", "2K", "4K" }, BuiltIn.ImageSizes.ToArray());
            Assert.Equal("1K", BuiltIn.DefaultImageSize);
            Assert.Equal(new[] { "1:1", "4:3", "3:2", "16:9" }, BuiltIn.AspectRatios.Take(4).ToArray());
            foreach (CatalogModel model in BuiltIn.Providers.SelectMany(p => p.Models))
            {
                Assert.All(model.Capabilities!.AspectRatios, r => Assert.Contains(r, BuiltIn.AspectRatios));
                Assert.All(model.Capabilities.ImageSizes, size => Assert.Contains(size, BuiltIn.ImageSizes));
            }
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
            ModelInfo model = BuiltIn.ResolveModel("gemini", "gemini-3-pro-image");

            Assert.Equal("Nano Banana Pro", model.DisplayName);
            Assert.Equal(14, model.Capabilities.MaxInputImages);
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
            ModelInfo first = BuiltIn.ResolveModel("gemini", "gemini-3-pro-image");
            first.Capabilities.MaxInputImages = 99;

            Assert.Equal(14, BuiltIn.ResolveModel("gemini", "gemini-3-pro-image").Capabilities.MaxInputImages);
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
