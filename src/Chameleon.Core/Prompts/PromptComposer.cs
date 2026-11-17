using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chameleon.Core.Generation;

namespace Chameleon.Core.Prompts
{
    /// <summary>
    /// The only place where the final prompt text is assembled. Adapters send its output unchanged.
    /// </summary>
    public sealed class PromptComposer
    {
        // Role-specific sentences, in the order they are appended.
        private static readonly KeyValuePair<ReferenceRole, string>[] RoleTexts =
        {
            new KeyValuePair<ReferenceRole, string>(ReferenceRole.Style, PromptTexts.StyleReference),
            new KeyValuePair<ReferenceRole, string>(ReferenceRole.Material, PromptTexts.MaterialReference),
            new KeyValuePair<ReferenceRole, string>(ReferenceRole.Object, PromptTexts.ObjectReference),
            new KeyValuePair<ReferenceRole, string>(ReferenceRole.Consistency, PromptTexts.Consistency)
        };

        /// <summary>Builds the text sent to the model.</summary>
        /// <remarks>
        /// Sections, separated by a blank line: system prompt, user prompt, one sentence per
        /// reference role in use, then the user's notes on individual references.
        /// </remarks>
        public string Compose(GenerationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var sections = new List<string>();

            if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            {
                sections.Add(request.SystemPrompt!);
            }

            sections.Add(request.Prompt ?? string.Empty);

            foreach (KeyValuePair<ReferenceRole, string> roleText in RoleTexts)
            {
                if (request.References.Any(r => r.Role == roleText.Key))
                {
                    sections.Add(roleText.Value);
                }
            }

            string? notes = ComposeNotes(request.GetOrderedReferences());
            if (notes != null)
            {
                sections.Add(notes);
            }

            return string.Join(PromptTexts.SectionSeparator, sections);
        }

        private static string? ComposeNotes(IReadOnlyList<ReferenceImage> orderedReferences)
        {
            if (!orderedReferences.Any(r => !string.IsNullOrWhiteSpace(r.Note)))
            {
                return null;
            }

            var sb = new StringBuilder(PromptTexts.ReferenceNotesHeading);
            for (int i = 0; i < orderedReferences.Count; i++)
            {
                ReferenceImage reference = orderedReferences[i];
                if (!string.IsNullOrWhiteSpace(reference.Note))
                {
                    sb.Append('\n')
                      .Append(i + 1)
                      .Append(". (")
                      .Append(reference.Role.ToString().ToLowerInvariant())
                      .Append(") ")
                      .Append(reference.Note!.Trim());
                }
            }

            return sb.ToString();
        }
    }
}
