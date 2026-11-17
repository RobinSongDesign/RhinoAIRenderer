namespace Chameleon.Core.Settings
{
    /// <summary>How batch renders of several views are scheduled.</summary>
    public sealed class BatchSettings
    {
        /// <summary>Views render one after another, each seeing all earlier results. Most consistent.</summary>
        public const string ModeSerial = "serial";

        /// <summary>The first view renders alone; the rest run in parallel and reference it.</summary>
        public const string ModeAnchorParallel = "anchorParallel";

        /// <summary>Views render independently of each other.</summary>
        public const string ModeIndependent = "independent";

        /// <summary>One of the Mode constants.</summary>
        public string Mode { get; set; } = ModeSerial;

        /// <summary>Upper bound on earlier results attached to a request as consistency references.</summary>
        public int MaxConsistencyRefs { get; set; } = 3;
    }
}
