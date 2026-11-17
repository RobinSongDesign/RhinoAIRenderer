namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "jobs.cancel": one of the two ids.</summary>
    public sealed class CancelParams
    {
        /// <summary>Job to cancel.</summary>
        public string? JobId { get; set; }

        /// <summary>Group to cancel.</summary>
        public string? GroupId { get; set; }
    }
}
