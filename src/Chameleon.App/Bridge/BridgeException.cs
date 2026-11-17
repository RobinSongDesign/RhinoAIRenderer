using System;

namespace Chameleon.App.Bridge
{
    /// <summary>A failure raised by the host itself, carrying the bridge error code to report.</summary>
    public sealed class BridgeException : Exception
    {
        /// <summary>Creates a failure.</summary>
        /// <param name="code">One of <see cref="BridgeErrorCodes"/>.</param>
        /// <param name="message">English message for logs.</param>
        /// <param name="detail">Optional detail for the UI.</param>
        public BridgeException(string code, string message, string? detail = null)
            : base(message)
        {
            Code = code;
            Detail = detail;
        }

        /// <summary>Bridge error code.</summary>
        public string Code { get; }

        /// <summary>Optional detail for the UI.</summary>
        public string? Detail { get; }

        /// <summary>Shorthand for an invalid_request failure.</summary>
        public static BridgeException InvalidRequest(string message)
        {
            return new BridgeException(BridgeErrorCodes.InvalidRequest, message);
        }

        /// <summary>Shorthand for a not_found failure.</summary>
        public static BridgeException NotFound(string message)
        {
            return new BridgeException(BridgeErrorCodes.NotFound, message);
        }
    }
}
