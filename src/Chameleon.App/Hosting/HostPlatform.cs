namespace Chameleon.App.Hosting
{
    /// <summary>Operating system family the application is running on.</summary>
    public enum HostPlatform
    {
        /// <summary>Microsoft Windows.</summary>
        Windows,

        /// <summary>Apple macOS.</summary>
        MacOS,

        /// <summary>Anything else; only used by the development host and tests.</summary>
        Other
    }
}
