namespace Chameleon.Core.Jobs
{
    /// <summary>Lifecycle of a generation job.</summary>
    public enum JobState
    {
        /// <summary>Waiting for a free slot or for the jobs it depends on.</summary>
        Queued,

        /// <summary>The request is with the backend.</summary>
        Running,

        /// <summary>Finished with at least one image.</summary>
        Succeeded,

        /// <summary>Finished with an error.</summary>
        Failed,

        /// <summary>Cancelled before or while running.</summary>
        Cancelled
    }
}
