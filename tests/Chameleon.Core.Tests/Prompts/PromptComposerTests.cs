using Chameleon.Core.Generation;
using Chameleon.Core.Prompts;
using Xunit;

namespace Chameleon.Core.Tests.Prompts
{
    public class PromptComposerTests
    {
        // Literal copies of the strings built by legacy/Services/AIRenderService.cs.
        private const string LegacyStyleSuffix =
            "\n\nA style reference image is also provided — match its lighting, atmosphere, and visual style.";

        private const string LegacyConsistencySuffix =
            "\n\nUsing the provided reference rendered image(s) as a strict style guide: " +
            "maintain identical lighting direction, shadow angles, material textures, " +
            "color palette, atmospheric mood, and positions of any people or objects. " +
            "Only change the camera position and angle to match the new architectural viewport shown. " +
            "The scene contents, lighting setup, and visual style must remain completely consistent across all views.";

        private readonly PromptComposer _composer = new PromptComposer();

        private static GenerationRequest Request(string? system = "SYS", string prompt = "PROMPT")
        {
            return new GenerationRequest { ModelId = "m", Prompt = prompt, SystemPrompt = system, Source = TestImages.Png() };
        }

        [Fact]
        public void Compose_SystemAndUserPrompt_MatchesLegacy()
        {
            Assert.Equal("SYS\n\nPROMPT", _composer.Compose(Request()));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Compose_WithoutSystemPrompt_IsJustThePrompt(string? system)
        {
            Assert.Equal("PROMPT", _composer.Compose(Request(system)));
        }

        [Fact]
        public void Compose_WithStyleReference_MatchesLegacy()
        {
            GenerationRequest request = Request();
            request.References.Add(new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Style));

            Assert.Equal("SYS\n\nPROMPT" + LegacyStyleSuffix, _composer.Compose(request));
        }

        [Fact]
        public void Compose_WithConsistencyReferences_MatchesLegacy()
        {
            GenerationRequest request = Request();
            request.References.Add(new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Consistency));
            request.References.Add(new ReferenceImage(TestImages.Webp(), ReferenceRole.Consistency));

            Assert.Equal("SYS\n\nPROMPT" + LegacyConsistencySuffix, _composer.Compose(request));
        }

        [Fact]
        public void Compose_WithStyleAndConsistency_ExplainsBoth()
        {
            // Deliberate change from the WPF version, which attached the style image in chained
            // renders without telling the model what it was.
            GenerationRequest request = Request();
            request.References.Add(new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Consistency));
            request.References.Add(new ReferenceImage(TestImages.Webp(), ReferenceRole.Style));

            Assert.Equal("SYS\n\nPROMPT" + LegacyStyleSuffix + LegacyConsistencySuffix, _composer.Compose(request));
        }

        [Fact]
        public void Compose_EachRoleSentenceAppearsOnce()
        {
            GenerationRequest request = Request(null);
            request.References.Add(new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Material));
            request.References.Add(new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Material));
            request.References.Add(new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Object));

            string text = _composer.Compose(request);

            Assert.Equal(
                "PROMPT\n\n" + PromptTexts.MaterialReference + "\n\n" + PromptTexts.ObjectReference,
                text);
        }

        [Fact]
        public void Compose_ListsNotesByPositionInSendOrder()
        {
            GenerationRequest request = Request(null);
            request.References.Add(new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Material, " oak floor "));
            request.References.Add(new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Style));
            request.References.Add(new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Consistency, "previous view"));

            string text = _composer.Compose(request);

            // Consistency references are sent first, so the note numbering follows that order.
            Assert.EndsWith(
                PromptTexts.ReferenceNotesHeading + "\n1. (consistency) previous view\n2. (material) oak floor",
                text);
        }

        [Fact]
        public void GetOrderedReferences_PutsConsistencyFirstAndKeepsRelativeOrder()
        {
            GenerationRequest request = Request();
            var style = new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Style);
            var first = new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Consistency);
            var material = new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Material);
            var second = new ReferenceImage(TestImages.Jpeg(), ReferenceRole.Consistency);
            request.References.Add(style);
            request.References.Add(first);
            request.References.Add(material);
            request.References.Add(second);

            Assert.Equal(new[] { first, second, style, material }, request.GetOrderedReferences());
        }
    }
}
