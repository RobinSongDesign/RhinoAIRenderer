namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "providers.signIn". The password is used once and never stored or logged.</summary>
    public sealed class SignInParams
    {
        /// <summary>Provider id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Account name or e-mail.</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>Account password.</summary>
        public string Password { get; set; } = string.Empty;
    }
}
