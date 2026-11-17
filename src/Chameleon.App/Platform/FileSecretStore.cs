using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Chameleon.Core.Settings;

namespace Chameleon.App.Platform
{
    /// <summary>
    /// Base for secret stores that keep one protected file per secret. File names are hashes of
    /// the logical name, so the directory listing reveals nothing about what is stored.
    /// </summary>
    public abstract class FileSecretStore : ISecretStore
    {
        private readonly string _directory;
        private readonly object _gate = new object();

        /// <summary>Creates a store over a directory, which is created on first write.</summary>
        protected FileSecretStore(string directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        /// <inheritdoc />
        public string? Get(string name)
        {
            lock (_gate)
            {
                string path = PathFor(name);
                if (!File.Exists(path))
                {
                    return null;
                }

                try
                {
                    byte[]? plain = Unprotect(File.ReadAllBytes(path));
                    return plain == null ? null : Encoding.UTF8.GetString(plain);
                }
                catch (IOException)
                {
                    return null;
                }
                catch (CryptographicException)
                {
                    // Written by another user or machine, or damaged: treat as absent.
                    return null;
                }
            }
        }

        /// <inheritdoc />
        public void Set(string name, string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            lock (_gate)
            {
                Directory.CreateDirectory(_directory);
                File.WriteAllBytes(PathFor(name), Protect(Encoding.UTF8.GetBytes(value)));
            }
        }

        /// <inheritdoc />
        public void Delete(string name)
        {
            lock (_gate)
            {
                string path = PathFor(name);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        /// <inheritdoc />
        public bool Contains(string name)
        {
            return Get(name) != null;
        }

        /// <summary>Encrypts a secret for storage.</summary>
        protected abstract byte[] Protect(byte[] plain);

        /// <summary>Decrypts a stored secret; returns null when the data cannot be trusted.</summary>
        protected abstract byte[]? Unprotect(byte[] stored);

        private string PathFor(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A secret name is required.", nameof(name));
            }

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(name));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return Path.Combine(_directory, sb + ".bin");
            }
        }
    }
}
