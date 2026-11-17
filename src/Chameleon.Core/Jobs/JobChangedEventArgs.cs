using System;

namespace Chameleon.Core.Jobs
{
    /// <summary>Raised whenever a job is created or changes state.</summary>
    public sealed class JobChangedEventArgs : EventArgs
    {
        /// <summary>Creates the event data.</summary>
        public JobChangedEventArgs(GenerationJob job)
        {
            Job = job ?? throw new ArgumentNullException(nameof(job));
        }

        /// <summary>Snapshot of the job after the change.</summary>
        public GenerationJob Job { get; }
    }
}
