using System;
using Chameleon.App.Hosting;
using Chameleon.Core.Settings;

namespace Chameleon.App.Platform
{
    /// <summary>Picks the best available secret store for the platform.</summary>
    public static class SecretStoreFactory
    {
        private const string ProbeName = "chameleon:self-test";

        /// <summary>
        /// Creates the platform's secret store: DPAPI on Windows, the login keychain on macOS, and
        /// an encrypted file elsewhere or whenever the OS store does not pass a round-trip check.
        /// </summary>
        public static ISecretStore Create(HostPlatform platform, IAppPaths paths, IEnvironmentInfo environment)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            if (environment == null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            ISecretStore? native = null;
            if (platform == HostPlatform.Windows)
            {
                native = new WindowsDpapiSecretStore(paths.SecretsDirectory);
            }
            else if (platform == HostPlatform.MacOS)
            {
                native = new MacKeychainSecretStore();
            }

            if (native != null && Works(native))
            {
                return native;
            }

            return new EncryptedFileSecretStore(
                paths.SecretsDirectory,
                environment.MachineName + "|" + environment.UserName);
        }

        /// <summary>Checks that a store can write, read back and delete a value.</summary>
        public static bool Works(ISecretStore store)
        {
            try
            {
                string value = Guid.NewGuid().ToString("N");
                store.Set(ProbeName, value);
                bool ok = store.Get(ProbeName) == value;
                store.Delete(ProbeName);
                return ok && store.Get(ProbeName) == null;
            }
            catch (Exception)
            {
                // Any failure (missing library, denied access, unsupported platform) means "do not use".
                return false;
            }
        }
    }
}
