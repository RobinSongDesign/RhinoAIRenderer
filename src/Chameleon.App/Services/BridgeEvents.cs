namespace Chameleon.App.Services
{
    /// <summary>Names of the events the host pushes to the UI.</summary>
    public static class BridgeEvents
    {
        /// <summary>A job was created or changed state.</summary>
        public const string JobUpdated = "job.updated";

        /// <summary>The backend list, readiness or selection changed.</summary>
        public const string BackendsChanged = "backends.changed";

        /// <summary>User settings changed.</summary>
        public const string SettingsChanged = "settings.changed";
    }
}
