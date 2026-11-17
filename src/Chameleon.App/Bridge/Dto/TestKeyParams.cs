namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "providers.testKey".</summary>
    public sealed class TestKeyParams
    {
        /// <summary>Provider id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Key to test; null tests the stored key.</summary>
        public string? Key { get; set; }

        /// <summary>Model to test with; null uses the provider's default.</summary>
        public string? ModelId { get; set; }
    }
}
