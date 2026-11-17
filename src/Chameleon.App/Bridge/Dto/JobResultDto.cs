using System.Collections.Generic;
using Chameleon.App.Assets;

namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Outcome of a successful job.</summary>
    public sealed class JobResultDto
    {
        /// <summary>Generated images.</summary>
        public IReadOnlyList<AssetRef> Assets { get; set; } = new List<AssetRef>();

        /// <summary>Text returned by the model, if any.</summary>
        public string? Text { get; set; }

        /// <summary>Credits charged, in account mode.</summary>
        public int? CreditsCharged { get; set; }

        /// <summary>History nodes created for the images (phase 3).</summary>
        public IReadOnlyList<string>? NodeIds { get; set; }
    }
}
