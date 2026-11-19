using System;
using System.Collections.Generic;

namespace Chameleon.Core.Settings
{
    /// <summary>Image models a built-in provider reported for the user's key, and when it was asked.</summary>
    public sealed class DiscoveredModels
    {
        /// <summary>Model ids in the order the provider listed them.</summary>
        public List<string> Models { get; set; } = new List<string>();

        /// <summary>When the provider was last asked.</summary>
        public DateTimeOffset CheckedAt { get; set; }
    }
}
