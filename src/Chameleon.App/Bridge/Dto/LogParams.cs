namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "log.write".</summary>
    public sealed class LogParams
    {
        /// <summary>"info", "warn" or "error".</summary>
        public string Level { get; set; } = "info";

        /// <summary>Message text.</summary>
        public string Message { get; set; } = string.Empty;
    }
}
