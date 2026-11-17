using System;
using System.Reflection;
using Chameleon.App;
using Chameleon.App.Hosting;
using Chameleon.App.Platform;
using Chameleon.App.Server;
using Chameleon.Rhino.Hosting;
using Rhino;
using Rhino.PlugIns;

namespace Chameleon.Rhino
{
    /// <summary>
    /// The plug-in. Owns the running application: it is created when a command first needs it and
    /// lives until Rhino shuts down, so closing the window does not stop running jobs.
    /// </summary>
    public class ChameleonPlugin : PlugIn
    {
        private readonly object _gate = new object();
        private ChameleonApp? _app;
        private RhinoHost? _host;

        /// <summary>Created by Rhino; never instantiate it yourself.</summary>
        public ChameleonPlugin()
        {
            Instance = this;
        }

        /// <summary>The single plug-in instance.</summary>
        public static ChameleonPlugin? Instance { get; private set; }

        /// <summary>Returns the running application, starting it on first use.</summary>
        /// <exception cref="InvalidOperationException">The application could not start.</exception>
        public ChameleonApp GetOrStartApp()
        {
            lock (_gate)
            {
                if (_app != null)
                {
                    return _app;
                }

                HostPlatform platform = global::Rhino.Runtime.HostUtils.RunningOnOSX ? HostPlatform.MacOS : HostPlatform.Windows;
                AppPaths paths = AppPaths.ForPlatform(platform, new SystemEnvironmentInfo());
                var logger = new FileLogger(paths.LogsDirectory);
                _host = new RhinoHost(platform, AppVersion(), logger);
                _host.Log("Starting Chameleon " + AppVersion() + " in Rhino " + RhinoApp.Version + " (" + Environment.Version + ").");

                var options = new ChameleonAppOptions(_host, paths, new EmbeddedResourceFileProvider(typeof(ChameleonPlugin).Assembly, "ui/"));
                _app = ChameleonApp.Start(options);
                return _app;
            }
        }

        /// <summary>The Rhino host adapter; null until the application has started.</summary>
        internal RhinoHost? Host => _host;

        /// <inheritdoc />
        protected override void OnShutdown()
        {
            lock (_gate)
            {
                _app?.Dispose();
                _app = null;
            }

            base.OnShutdown();
        }

        private static string AppVersion()
        {
            Assembly assembly = typeof(ChameleonPlugin).Assembly;
            string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            // Strip the source revision suffix ("2.0.0+abc123") the SDK appends.
            return (informational ?? assembly.GetName().Version?.ToString() ?? "0.0.0").Split('+')[0];
        }
    }
}
