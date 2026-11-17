namespace Chameleon.Core.Settings
{
    /// <summary>Settings of the signed-in account mode.</summary>
    public sealed class CloudSettings
    {
        /// <summary>Gateway address override; null uses the built-in default.</summary>
        public string? BaseUrl { get; set; }
    }
}
