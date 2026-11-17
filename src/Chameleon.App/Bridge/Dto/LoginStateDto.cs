namespace Chameleon.App.Bridge.Dto
{
    /// <summary>State of a device sign-in.</summary>
    public sealed class LoginStateDto
    {
        /// <summary>Waiting for the user to confirm in the browser.</summary>
        public const string Pending = "pending";

        /// <summary>The sign-in ended without success; see <see cref="Error"/>.</summary>
        public const string Failed = "failed";

        /// <summary>"pending" or "failed".</summary>
        public string State { get; set; } = Pending;

        /// <summary>Code the user confirms in the browser, e.g. WDJB-MJHT.</summary>
        public string UserCode { get; set; } = string.Empty;

        /// <summary>Page where the code is confirmed (with the code filled in).</summary>
        public string VerificationUrl { get; set; } = string.Empty;

        /// <summary>Seconds the code stays valid, from the start of the sign-in.</summary>
        public int ExpiresIn { get; set; }

        /// <summary>Why the sign-in failed.</summary>
        public RpcError? Error { get; set; }
    }
}
