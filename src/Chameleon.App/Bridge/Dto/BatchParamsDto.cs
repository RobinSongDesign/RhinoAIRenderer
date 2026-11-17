using System.Collections.Generic;

namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "jobs.submitBatch".</summary>
    public sealed class BatchParamsDto
    {
        /// <summary>One entry per view, in render order.</summary>
        public List<GenerateParamsDto> Items { get; set; } = new List<GenerateParamsDto>();

        /// <summary>"serial", "anchorParallel" or "independent".</summary>
        public string Mode { get; set; } = "serial";
    }
}
