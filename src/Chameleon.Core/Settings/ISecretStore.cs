namespace Chameleon.Core.Settings
{
    /// <summary>
    /// Storage for secrets such as API keys and refresh tokens. Production implementations are
    /// backed by the operating system; nothing stored here may be written to the settings file.
    /// </summary>
    public interface ISecretStore
    {
        /// <summary>Returns the secret, or null when none is stored under the name.</summary>
        string? Get(string name);

        /// <summary>Stores or replaces a secret.</summary>
        void Set(string name, string value);

        /// <summary>Removes a secret; does nothing when it does not exist.</summary>
        void Delete(string name);

        /// <summary>True when a secret is stored under the name.</summary>
        bool Contains(string name);
    }
}
