using System;
using System.Collections.Generic;
using System.Linq;
using Chameleon.Core.Generation;
using Chameleon.Core.Prompts;

namespace Chameleon.Core.Jobs
{
    /// <summary>Turns the views of a batch into jobs with the dependencies its mode calls for.</summary>
    public static class BatchPlanner
    {
        /// <summary>Plans a batch.</summary>
        /// <param name="items">Views in render order.</param>
        /// <param name="mode">Requested mode.</param>
        /// <param name="capabilities">Capabilities of the model that will run the batch.</param>
        /// <returns>One plan entry per item, in the same order.</returns>
        public static IReadOnlyList<PlannedJob> Plan(
            IReadOnlyList<BatchItem> items,
            BatchMode mode,
            BackendCapabilities capabilities)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (capabilities == null)
            {
                throw new ArgumentNullException(nameof(capabilities));
            }

            BatchMode effectiveMode = GetEffectiveMode(mode, capabilities);
            var plan = new List<PlannedJob>(items.Count);

            for (int i = 0; i < items.Count; i++)
            {
                BatchItem item = items[i];
                GenerationRequest request = item.Request.Clone();
                request.Prompt = CombinePrompt(request.Prompt, item.AddonPrompt);

                IReadOnlyList<int> dependsOn;
                switch (effectiveMode)
                {
                    case BatchMode.Serial:
                        dependsOn = Enumerable.Range(0, i).ToArray();
                        break;
                    case BatchMode.AnchorParallel:
                        dependsOn = i == 0 ? Array.Empty<int>() : new[] { 0 };
                        break;
                    default:
                        dependsOn = Array.Empty<int>();
                        break;
                }

                plan.Add(new PlannedJob(request, dependsOn, item.Label));
            }

            return plan;
        }

        /// <summary>
        /// Returns the mode that will actually be used: a model that cannot take a second input image
        /// cannot receive consistency references, so its batches are always independent.
        /// </summary>
        public static BatchMode GetEffectiveMode(BatchMode requested, BackendCapabilities capabilities)
        {
            bool canTakeReferences = capabilities.MaxInputImages == 0 || capabilities.MaxInputImages >= 2;
            return canTakeReferences ? requested : BatchMode.Independent;
        }

        /// <summary>Appends a view's own prompt to the shared prompt, separated by a blank line.</summary>
        public static string CombinePrompt(string? sharedPrompt, string? addonPrompt)
        {
            string shared = sharedPrompt ?? string.Empty;
            if (string.IsNullOrWhiteSpace(addonPrompt))
            {
                return shared;
            }

            return shared.Length == 0 ? addonPrompt! : shared + PromptTexts.SectionSeparator + addonPrompt;
        }
    }
}
