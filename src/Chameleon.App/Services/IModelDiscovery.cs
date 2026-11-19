namespace Chameleon.App.Services
{
    /// <summary>Starts a model discovery without waiting for it (used when a key is saved).</summary>
    public interface IModelDiscovery
    {
        /// <summary>Asks the provider for its models in the background; failures are only logged.</summary>
        void RefreshInBackground(string providerId);
    }
}
