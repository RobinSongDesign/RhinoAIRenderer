using System.Collections.Generic;
using System.Linq;
using Chameleon.Core.Generation;
using Chameleon.Core.Jobs;
using Xunit;

namespace Chameleon.Core.Tests.Jobs
{
    public class BatchPlannerTests
    {
        private static List<BatchItem> Items(int count)
        {
            return Enumerable.Range(1, count)
                .Select(i => new BatchItem(
                    new GenerationRequest { ModelId = "m", Prompt = "shared", Source = TestImages.Png() },
                    i == 2 ? "only for view 2" : null,
                    "View " + i))
                .ToList();
        }

        private static int[][] Dependencies(IReadOnlyList<PlannedJob> plan)
        {
            return plan.Select(p => p.DependsOnIndexes.ToArray()).ToArray();
        }

        [Fact]
        public void Serial_EachJobDependsOnAllEarlierOnes()
        {
            IReadOnlyList<PlannedJob> plan = BatchPlanner.Plan(Items(4), BatchMode.Serial, new BackendCapabilities { MaxInputImages = 8 });

            Assert.Equal(new[] { new int[0], new[] { 0 }, new[] { 0, 1 }, new[] { 0, 1, 2 } }, Dependencies(plan));
            Assert.Equal(new[] { "View 1", "View 2", "View 3", "View 4" }, plan.Select(p => p.Label).ToArray());
        }

        [Fact]
        public void AnchorParallel_OnlyTheFirstJobIsADependency()
        {
            IReadOnlyList<PlannedJob> plan = BatchPlanner.Plan(Items(4), BatchMode.AnchorParallel, new BackendCapabilities());

            Assert.Equal(new[] { new int[0], new[] { 0 }, new[] { 0 }, new[] { 0 } }, Dependencies(plan));
        }

        [Fact]
        public void Independent_HasNoDependencies()
        {
            IReadOnlyList<PlannedJob> plan = BatchPlanner.Plan(Items(3), BatchMode.Independent, new BackendCapabilities());

            Assert.All(plan, p => Assert.Empty(p.DependsOnIndexes));
        }

        [Theory]
        [InlineData(BatchMode.Serial)]
        [InlineData(BatchMode.AnchorParallel)]
        public void ModelsThatTakeOneImage_AlwaysPlanIndependently(BatchMode requested)
        {
            // Replaces the legacy "is this the OpenAI format" check with a capability check.
            var singleImage = new BackendCapabilities { MaxInputImages = 1 };

            IReadOnlyList<PlannedJob> plan = BatchPlanner.Plan(Items(3), requested, singleImage);

            Assert.All(plan, p => Assert.Empty(p.DependsOnIndexes));
            Assert.Equal(BatchMode.Independent, BatchPlanner.GetEffectiveMode(requested, singleImage));
            Assert.Equal(requested, BatchPlanner.GetEffectiveMode(requested, new BackendCapabilities { MaxInputImages = 2 }));
            Assert.Equal(requested, BatchPlanner.GetEffectiveMode(requested, new BackendCapabilities { MaxInputImages = 0 }));
        }

        [Fact]
        public void AddonPrompt_IsAppendedAfterABlankLine_AndOriginalsAreUntouched()
        {
            List<BatchItem> items = Items(2);

            IReadOnlyList<PlannedJob> plan = BatchPlanner.Plan(items, BatchMode.Serial, new BackendCapabilities());

            Assert.Equal("shared", plan[0].Request.Prompt);
            Assert.Equal("shared\n\nonly for view 2", plan[1].Request.Prompt);
            Assert.Equal("shared", items[1].Request.Prompt);
            Assert.NotSame(items[0].Request, plan[0].Request);
        }

        [Theory]
        [InlineData("shared", null, "shared")]
        [InlineData("shared", "   ", "shared")]
        [InlineData("", "addon", "addon")]
        [InlineData(null, "addon", "addon")]
        [InlineData("shared", "addon", "shared\n\naddon")]
        public void CombinePrompt_HandlesEmptyParts(string? shared, string? addon, string expected)
        {
            Assert.Equal(expected, BatchPlanner.CombinePrompt(shared, addon));
        }

        [Fact]
        public void EmptyBatch_PlansNothing()
        {
            Assert.Empty(BatchPlanner.Plan(new List<BatchItem>(), BatchMode.Serial, new BackendCapabilities()));
        }
    }
}
