namespace Chameleon.App.Bridge
{
    /// <summary>Error part of an RPC response.</summary>
    public sealed class RpcError
    {
        /// <summary>One of <see cref="BridgeErrorCodes"/>; the UI localizes by this value.</summary>
        public string Code { get; set; } = BridgeErrorCodes.Internal;

        /// <summary>English message for logs.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Provider message or other detail the UI may show on demand.</summary>
        public string? Detail { get; set; }

        /// <summary>True when repeating the same call may succeed.</summary>
        public bool? Retryable { get; set; }
    }
}
