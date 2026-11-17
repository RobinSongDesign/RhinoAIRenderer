using System.Collections.Generic;
using Chameleon.Core.Generation;

namespace Chameleon.App.Bridge.Dto
{
    /// <summary>A backend as shown in the UI's backend list.</summary>
    public sealed class BackendInfoDto
    {
        /// <summary>Kind of a bring-your-own-key backend.</summary>
        public const string KindDirect = "direct";

        /// <summary>Kind of the signed-in account backend.</summary>
        public const string KindCloud = "cloud";

        /// <summary>Kind of backends that only exist in the development host.</summary>
        public const string KindDev = "dev";

        /// <summary>Backend id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>One of the Kind constants.</summary>
        public string Kind { get; set; } = KindDirect;

        /// <summary>Name shown to the user.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>True when the backend can be used now (key set, or signed in).</summary>
        public bool Ready { get; set; }

        /// <summary>Models the backend offers.</summary>
        public IReadOnlyList<ModelInfo> Models { get; set; } = new List<ModelInfo>();

        /// <summary>Model selected by default.</summary>
        public string DefaultModel { get; set; } = string.Empty;
    }
}
