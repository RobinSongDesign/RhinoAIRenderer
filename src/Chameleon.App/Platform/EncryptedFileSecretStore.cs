using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Chameleon.App.Platform
{
    /// <summary>
    /// Fallback secret store for systems without a usable OS key store. Secrets are encrypted
    /// with AES-256-CBC and authenticated with HMAC-SHA256 using a key derived from the machine
    /// and user names.
    /// </summary>
    /// <remarks>
    /// This keeps secrets out of plain-text files and backups, but it is not a defense against a
    /// malicious program running as the same user, which can derive the same key.
    /// </remarks>
    public sealed class EncryptedFileSecretStore : FileSecretStore
    {
        private const byte FormatVersion = 1;
        private const int IvLength = 16;
        private const int MacLength = 32;
        private const int KeyDerivationIterations = 100000;

        private static readonly byte[] Salt = Encoding.UTF8.GetBytes("Chameleon.EncryptedFileSecretStore.v1");

        private readonly byte[] _encryptionKey = new byte[32];
        private readonly byte[] _macKey = new byte[32];

        /// <summary>Creates a store.</summary>
        /// <param name="directory">Directory holding the encrypted files.</param>
        /// <param name="keyMaterial">Text the key is derived from, e.g. machine and user name.</param>
        public EncryptedFileSecretStore(string directory, string keyMaterial)
            : base(directory)
        {
            if (string.IsNullOrEmpty(keyMaterial))
            {
                throw new ArgumentException("Key material is required.", nameof(keyMaterial));
            }

            using (var kdf = new Rfc2898DeriveBytes(keyMaterial, Salt, KeyDerivationIterations))
            {
                byte[] derived = kdf.GetBytes(_encryptionKey.Length + _macKey.Length);
                Buffer.BlockCopy(derived, 0, _encryptionKey, 0, _encryptionKey.Length);
                Buffer.BlockCopy(derived, _encryptionKey.Length, _macKey, 0, _macKey.Length);
            }
        }

        /// <inheritdoc />
        protected override byte[] Protect(byte[] plain)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = _encryptionKey;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();

                byte[] cipher;
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                }

                // Layout: version | iv | ciphertext | hmac(version | iv | ciphertext)
                var output = new byte[1 + IvLength + cipher.Length + MacLength];
                output[0] = FormatVersion;
                Buffer.BlockCopy(aes.IV, 0, output, 1, IvLength);
                Buffer.BlockCopy(cipher, 0, output, 1 + IvLength, cipher.Length);

                byte[] mac = ComputeMac(output, output.Length - MacLength);
                Buffer.BlockCopy(mac, 0, output, output.Length - MacLength, MacLength);
                return output;
            }
        }

        /// <inheritdoc />
        protected override byte[]? Unprotect(byte[] stored)
        {
            if (stored.Length < 1 + IvLength + MacLength || stored[0] != FormatVersion)
            {
                return null;
            }

            int signedLength = stored.Length - MacLength;
            byte[] expected = ComputeMac(stored, signedLength);
            if (!FixedTimeEquals(expected, stored, signedLength))
            {
                return null;
            }

            using (Aes aes = Aes.Create())
            {
                aes.Key = _encryptionKey;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                var iv = new byte[IvLength];
                Buffer.BlockCopy(stored, 1, iv, 0, IvLength);
                aes.IV = iv;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(stored, 1 + IvLength, signedLength - 1 - IvLength);
                }
            }
        }

        private byte[] ComputeMac(byte[] data, int length)
        {
            using (var hmac = new HMACSHA256(_macKey))
            {
                return hmac.ComputeHash(data, 0, length);
            }
        }

        // Constant-time comparison so a forged file cannot be refined by timing the check.
        private static bool FixedTimeEquals(byte[] expected, byte[] actual, int actualOffset)
        {
            int difference = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                difference |= expected[i] ^ actual[actualOffset + i];
            }

            return difference == 0;
        }
    }
}
