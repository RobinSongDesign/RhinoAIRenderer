namespace Chameleon.Core.Settings
{
    /// <summary>Logical names under which secrets are stored.</summary>
    public static class SecretNames
    {
        /// <summary>Name of the cloud account refresh token.</summary>
        public const string CloudRefreshToken = "cloud:refreshToken";

        /// <summary>Name of the API key of a provider.</summary>
        public static string ForProvider(string profileId)
        {
            return "provider:" + profileId;
        }
    }
}
