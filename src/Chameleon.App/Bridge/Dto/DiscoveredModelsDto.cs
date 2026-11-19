using System.Collections.Generic;

namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Result of "providers.discoverModels".</summary>
    public sealed class DiscoveredModelsDto
    {
        /// <summary>Image models the provider offers, in its order; the provider now lists exactly these.</summary>
        public IReadOnlyList<string> Models { get; set; } = new List<string>();

        /// <summary>Models that were not listed before.</summary>
        public IReadOnlyList<string> Added { get; set; } = new List<string>();

        /// <summary>Models that were listed before and are no longer offered.</summary>
        public IReadOnlyList<string> Removed { get; set; } = new List<string>();
    }
}
