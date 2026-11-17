using System.Security.Cryptography;
using System.Text;

namespace Chameleon.App.Platform
{
    /// <summary>Secrets encrypted with Windows DPAPI for the current user. Windows only.</summary>
    public sealed class WindowsDpapiSecretStore : FileSecretStore
    {
        // Binds the ciphertext to this application so another program's DPAPI blob cannot be swapped in.
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Chameleon.SecretStore.v1");

        /// <summary>Creates a store over a directory.</summary>
        public WindowsDpapiSecretStore(string directory)
            : base(directory)
        {
        }

        /// <inheritdoc />
        protected override byte[] Protect(byte[] plain)
        {
            return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        }

        /// <inheritdoc />
        protected override byte[]? Unprotect(byte[] stored)
        {
            return ProtectedData.Unprotect(stored, Entropy, DataProtectionScope.CurrentUser);
        }
    }
}
