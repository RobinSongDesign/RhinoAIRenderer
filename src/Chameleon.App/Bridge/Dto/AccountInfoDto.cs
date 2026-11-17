namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Account state shown in the UI. Never carries tokens.</summary>
    public sealed class AccountInfoDto
    {
        /// <summary>True when a gateway address is configured, i.e. account mode can be used at all.</summary>
        public bool Available { get; set; }

        /// <summary>True while signed in.</summary>
        public bool SignedIn { get; set; }

        /// <summary>Email of the signed-in user.</summary>
        public string? Email { get; set; }

        /// <summary>Balance as last reported.</summary>
        public int? Credits { get; set; }

        /// <summary>The sign-in in progress or its last failure; null when idle.</summary>
        public LoginStateDto? Login { get; set; }
    }
}
