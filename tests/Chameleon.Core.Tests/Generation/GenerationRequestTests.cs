using Chameleon.Core.Generation;
using Xunit;

namespace Chameleon.Core.Tests.Generation
{
    public class GenerationRequestTests
    {
        private static BackendCapabilities Caps() => new BackendCapabilities
        {
            SupportsMask = false,
            MaxInputImages = 3,
            ImageSizes = new[] { "1K", "2K" },
            AspectRatios = new[] { "1:1", "16:9" }
        };

        private static GenerationRequest ValidRequest() => new GenerationRequest
        {
            ModelId = "model-a",
            Prompt = "render it",
            Source = TestImages.Png(),
            ImageSize = "1K"
        };

        private static void AssertInvalid(GenerationRequest request, BackendCapabilities caps, string expectedFragment)
        {
            RenderException ex = Assert.Throws<RenderException>(() => request.Validate(caps));
            Assert.Equal(RenderErrorCode.InvalidRequest, ex.Code);
            Assert.Contains(expectedFragment, ex.Message);
            Assert.False(ex.IsRetryable);
        }

        [Fact]
        public void Validate_AcceptsWellFormedRequest()
        {
            GenerationRequest request = ValidRequest();
            request.AspectRatio = "16:9";
            request.References.Add(new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Style));

            request.Validate(Caps());
        }

        [Fact]
        public void Validate_RejectsMissingModel()
        {
            GenerationRequest request = ValidRequest();
            request.ModelId = " ";
            AssertInvalid(request, Caps(), "model");
        }

        [Fact]
        public void Validate_RejectsBlankPrompt()
        {
            GenerationRequest request = ValidRequest();
            request.Prompt = "  \n ";
            AssertInvalid(request, Caps(), "prompt");
        }

        [Fact]
        public void Validate_RejectsUnsupportedImageSize()
        {
            GenerationRequest request = ValidRequest();
            request.ImageSize = "4K";
            AssertInvalid(request, Caps(), "4K");
        }

        [Fact]
        public void Validate_RejectsUnsupportedAspectRatio()
        {
            GenerationRequest request = ValidRequest();
            request.AspectRatio = "21:9";
            AssertInvalid(request, Caps(), "21:9");
        }

        [Fact]
        public void Validate_TreatsEmptyAspectRatioAsKeepSource()
        {
            GenerationRequest request = ValidRequest();
            request.AspectRatio = "";
            request.Validate(Caps());
        }

        [Fact]
        public void Validate_RejectsTooManyInputImages()
        {
            GenerationRequest request = ValidRequest();
            for (int i = 0; i < 3; i++)
            {
                request.References.Add(new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Consistency));
            }

            Assert.Equal(4, request.InputImageCount);
            AssertInvalid(request, Caps(), "Too many input images");
        }

        [Fact]
        public void Validate_RejectsMaskWhenUnsupported()
        {
            GenerationRequest request = ValidRequest();
            request.Mask = TestImages.Png();
            AssertInvalid(request, Caps(), "mask");
        }

        [Fact]
        public void Validate_RejectsMaskWithoutSource()
        {
            BackendCapabilities caps = Caps();
            caps.SupportsMask = true;
            GenerationRequest request = ValidRequest();
            request.Source = null;
            request.Mask = TestImages.Png();
            AssertInvalid(request, caps, "source");
        }

        [Fact]
        public void Validate_EmptyCapabilityListsMeanAnyValue()
        {
            GenerationRequest request = ValidRequest();
            request.ImageSize = "8K";
            request.AspectRatio = "7:3";
            request.Validate(new BackendCapabilities());
        }

        [Fact]
        public void Clone_CopiesValuesAndDetachesCollections()
        {
            GenerationRequest original = ValidRequest();
            original.SystemPrompt = "sys";
            original.AspectRatio = "1:1";
            original.Extras["k"] = "v";
            original.References.Add(new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Style, "note"));

            GenerationRequest copy = original.Clone();
            copy.References.Add(new ReferenceImage(TestImages.Webp(), ReferenceRole.Consistency));
            copy.Extras["k2"] = "v2";

            Assert.Equal("model-a", copy.ModelId);
            Assert.Equal("sys", copy.SystemPrompt);
            Assert.Equal("1:1", copy.AspectRatio);
            Assert.Same(original.Source, copy.Source);
            Assert.Single(original.References);
            Assert.Equal(2, copy.References.Count);
            Assert.Single(original.Extras);
        }
    }
}
