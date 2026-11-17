namespace Chameleon.App.Hosting
{
    /// <summary>Facts about the process hosting the application.</summary>
    public sealed class HostInfo
    {
        /// <summary>Creates the description.</summary>
        /// <param name="platform">Operating system family.</param>
        /// <param name="rhinoVersion">Rhino version text; empty outside Rhino.</param>
        /// <param name="appVersion">Version of the plug-in or development host.</param>
        public HostInfo(HostPlatform platform, string rhinoVersion, string appVersion)
        {
            Platform = platform;
            RhinoVersion = rhinoVersion ?? string.Empty;
            AppVersion = appVersion ?? string.Empty;
        }

        /// <summary>Operating system family.</summary>
        public HostPlatform Platform { get; }

        /// <summary>Rhino version text; empty outside Rhino.</summary>
        public string RhinoVersion { get; }

        /// <summary>Version of the plug-in or development host.</summary>
        public string AppVersion { get; }
    }
}
