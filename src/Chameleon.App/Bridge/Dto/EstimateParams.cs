namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "account.estimate".</summary>
    public sealed class EstimateParams
    {
        /// <summary>Model to price.</summary>
        public string ModelId { get; set; } = string.Empty;

        /// <summary>Size tier to price.</summary>
        public string ImageSize { get; set; } = string.Empty;

        /// <summary>Number of generations (multi-candidate); defaults to 1.</summary>
        public int Count { get; set; } = 1;
    }
}
