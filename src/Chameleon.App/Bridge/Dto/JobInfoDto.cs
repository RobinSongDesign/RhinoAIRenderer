using System;

namespace Chameleon.App.Bridge.Dto
{
    /// <summary>A job as seen by the UI.</summary>
    public sealed class JobInfoDto
    {
        /// <summary>Job id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Group id, if any.</summary>
        public string? GroupId { get; set; }

        /// <summary>Human-readable label, if any.</summary>
        public string? Label { get; set; }

        /// <summary>"queued", "running", "succeeded", "failed" or "cancelled".</summary>
        public string State { get; set; } = string.Empty;

        /// <summary>When the job was created.</summary>
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>Set once the job succeeded.</summary>
        public JobResultDto? Result { get; set; }

        /// <summary>Set once the job failed.</summary>
        public RpcError? Error { get; set; }
    }
}
