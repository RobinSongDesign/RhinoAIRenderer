using System;
using System.Collections.Concurrent;

namespace Chameleon.Core.Settings
{
    /// <summary>Secret store that lives only in memory. Used for tests and one-off key checks.</summary>
    public sealed class InMemorySecretStore : ISecretStore
    {
        private readonly ConcurrentDictionary<string, string> _secrets =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        /// <inheritdoc />
        public string? Get(string name)
        {
            return _secrets.TryGetValue(name, out string? value) ? value : null;
        }

        /// <inheritdoc />
        public void Set(string name, string value)
        {
            _secrets[name] = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <inheritdoc />
        public void Delete(string name)
        {
            _secrets.TryRemove(name, out _);
        }

        /// <inheritdoc />
        public bool Contains(string name)
        {
            return _secrets.ContainsKey(name);
        }
    }
}
