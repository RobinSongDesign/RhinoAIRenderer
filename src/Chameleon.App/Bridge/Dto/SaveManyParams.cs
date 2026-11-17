using System.Collections.Generic;

namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "files.saveMany".</summary>
    public sealed class SaveManyParams
    {
        /// <summary>Files to write.</summary>
        public List<SaveManyItem> Items { get; set; } = new List<SaveManyItem>();
    }
}
