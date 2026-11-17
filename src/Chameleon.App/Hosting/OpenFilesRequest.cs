using System.Collections.Generic;

namespace Chameleon.App.Hosting
{
    /// <summary>Parameters of a native "open file" dialog.</summary>
    public sealed class OpenFilesRequest
    {
        /// <summary>True to let the user pick several files.</summary>
        public bool Multiple { get; set; }

        /// <summary>Accepted extensions, without dots.</summary>
        public IList<string> Extensions { get; } = new List<string>();
    }
}
