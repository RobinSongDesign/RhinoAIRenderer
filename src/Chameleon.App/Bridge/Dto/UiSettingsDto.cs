using Chameleon.Core.Settings;

namespace Chameleon.App.Bridge.Dto
{
    /// <summary>The settings the UI can read and change.</summary>
    public sealed class UiSettingsDto
    {
        /// <summary>"zh" or "en".</summary>
        public string Language { get; set; } = string.Empty;

        /// <summary>User-defined system prompt.</summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Include)]
        public string? SystemPrompt { get; set; }

        /// <summary>Batch options.</summary>
        public BatchSettings Batch { get; set; } = new BatchSettings();

        /// <summary>Builds the DTO from stored settings.</summary>
        public static UiSettingsDto From(AppSettings settings)
        {
            return new UiSettingsDto
            {
                Language = settings.Language,
                SystemPrompt = settings.SystemPrompt,
                Batch = new BatchSettings { Mode = settings.Batch.Mode, MaxConsistencyRefs = settings.Batch.MaxConsistencyRefs }
            };
        }
    }
}
