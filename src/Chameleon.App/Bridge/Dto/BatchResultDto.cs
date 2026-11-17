using System.Collections.Generic;

namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Result of "jobs.submitBatch".</summary>
    public sealed class BatchResultDto
    {
        /// <summary>Group id shared by the batch's jobs.</summary>
        public string GroupId { get; set; } = string.Empty;

        /// <summary>The created jobs, in item order.</summary>
        public IReadOnlyList<JobInfoDto> Jobs { get; set; } = new List<JobInfoDto>();
    }
}
