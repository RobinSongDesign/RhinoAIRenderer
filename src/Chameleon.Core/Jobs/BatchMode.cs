namespace Chameleon.Core.Jobs
{
    /// <summary>How the jobs of a batch relate to each other.</summary>
    public enum BatchMode
    {
        /// <summary>One after another; each job sees all earlier results. Slowest, most consistent.</summary>
        Serial,

        /// <summary>The first job runs alone; all others run in parallel and reference only it.</summary>
        AnchorParallel,

        /// <summary>No dependencies; everything runs in parallel.</summary>
        Independent
    }
}
