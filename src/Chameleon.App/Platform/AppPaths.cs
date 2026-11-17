using System;
using Chameleon.App.Hosting;
using Chameleon.Core;

namespace Chameleon.App.Platform
{
    /// <summary>Default file locations for each platform.</summary>
    public sealed class AppPaths : IAppPaths
    {
        private const string LegacyFolderName = "AIRenderer";

        private AppPaths(
            string settingsFile,
            string projectsDirectory,
            string cacheDirectory,
            string logsDirectory,
            string secretsDirectory,
            string? legacySettingsFile)
        {
            SettingsFile = settingsFile;
            ProjectsDirectory = projectsDirectory;
            CacheDirectory = cacheDirectory;
            LogsDirectory = logsDirectory;
            SecretsDirectory = secretsDirectory;
            LegacySettingsFile = legacySettingsFile;
        }

        /// <inheritdoc />
        public string SettingsFile { get; }

        /// <inheritdoc />
        public string ProjectsDirectory { get; }

        /// <inheritdoc />
        public string CacheDirectory { get; }

        /// <inheritdoc />
        public string LogsDirectory { get; }

        /// <inheritdoc />
        public string SecretsDirectory { get; }

        /// <inheritdoc />
        public string? LegacySettingsFile { get; }

        /// <summary>Returns the standard locations for a platform.</summary>
        /// <exception cref="InvalidOperationException">A required environment variable is missing.</exception>
        public static AppPaths ForPlatform(HostPlatform platform, IEnvironmentInfo environment)
        {
            if (environment == null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            string product = ChameleonInfo.ProductName;
            switch (platform)
            {
                case HostPlatform.Windows:
                {
                    const char sep = '\\';
                    string roaming = Require(environment, "APPDATA");
                    string local = environment.GetVariable("LOCALAPPDATA") ?? roaming;
                    string roamingRoot = Join(sep, roaming, product);
                    string localRoot = Join(sep, local, product);
                    return new AppPaths(
                        Join(sep, roamingRoot, "settings.json"),
                        Join(sep, localRoot, "projects"),
                        Join(sep, localRoot, "cache"),
                        Join(sep, localRoot, "logs"),
                        Join(sep, roamingRoot, "secrets"),
                        Join(sep, roaming, LegacyFolderName, "settings.json"));
                }

                case HostPlatform.MacOS:
                {
                    const char sep = '/';
                    string library = Join(sep, Require(environment, "HOME"), "Library");
                    string support = Join(sep, library, "Application Support", product);
                    return new AppPaths(
                        Join(sep, support, "settings.json"),
                        Join(sep, support, "projects"),
                        Join(sep, library, "Caches", product),
                        Join(sep, library, "Logs", product),
                        Join(sep, support, "secrets"),
                        null);
                }

                default:
                {
                    const char sep = '/';
                    string home = Require(environment, "HOME");
                    string config = environment.GetVariable("XDG_CONFIG_HOME") ?? Join(sep, home, ".config");
                    string root = Join(sep, config, product);
                    return new AppPaths(
                        Join(sep, root, "settings.json"),
                        Join(sep, root, "projects"),
                        Join(sep, home, ".cache", product),
                        Join(sep, root, "logs"),
                        Join(sep, root, "secrets"),
                        null);
                }
            }
        }

        /// <summary>Puts every location under one directory. Used by the development host and tests.</summary>
        public static AppPaths UnderRoot(string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                throw new ArgumentException("A root directory is required.", nameof(root));
            }

            char sep = System.IO.Path.DirectorySeparatorChar;
            return new AppPaths(
                Join(sep, root, "settings.json"),
                Join(sep, root, "projects"),
                Join(sep, root, "cache"),
                Join(sep, root, "logs"),
                Join(sep, root, "secrets"),
                null);
        }

        private static string Require(IEnvironmentInfo environment, string variable)
        {
            string? value = environment.GetVariable(variable);
            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException("Environment variable '" + variable + "' is not set.");
            }

            return value!;
        }

        // Joins with an explicit separator so paths for one platform can be computed (and tested) on another.
        private static string Join(char separator, params string[] parts)
        {
            string result = parts[0].TrimEnd('/', '\\');
            for (int i = 1; i < parts.Length; i++)
            {
                result += separator + parts[i];
            }

            return result;
        }
    }
}
