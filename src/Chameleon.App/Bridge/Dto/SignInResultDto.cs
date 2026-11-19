namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Result of "providers.signIn" and "providers.signInVerify".</summary>
    public sealed class SignInResultDto
    {
        /// <summary>Signed in; the provider's key is stored.</summary>
        public const string StatusSignedIn = "signedIn";

        /// <summary>The account wants a two-factor code; call "providers.signInVerify".</summary>
        public const string StatusNeedsTwoFactor = "needsTwoFactor";

        /// <summary>One of the Status constants.</summary>
        public string Status { get; set; } = StatusSignedIn;

        /// <summary>Signed-in account name.</summary>
        public string? Username { get; set; }

        /// <summary>Handle of the pending two-factor step.</summary>
        public string? FlowToken { get; set; }
    }
}
