namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "providers.setKey".</summary>
    public sealed class SetKeyParams
    {
        /// <summary>Provider id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The key; empty clears the stored key.</summary>
        public string Key { get; set; } = string.Empty;
    }
}
