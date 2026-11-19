namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "providers.signInVerify".</summary>
    public sealed class SignInVerifyParams
    {
        /// <summary>Provider id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The flow token "providers.signIn" returned.</summary>
        public string FlowToken { get; set; } = string.Empty;

        /// <summary>The code from the authenticator app (or a backup code).</summary>
        public string Code { get; set; } = string.Empty;
    }
}
