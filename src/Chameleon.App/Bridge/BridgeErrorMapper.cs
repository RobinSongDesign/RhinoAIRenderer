using System;
using System.Collections.Generic;
using Chameleon.App.Hosting;
using Chameleon.Core.Generation;
using Newtonsoft.Json;

namespace Chameleon.App.Bridge
{
    /// <summary>The single place where exceptions become bridge errors.</summary>
    public static class BridgeErrorMapper
    {
        /// <summary>Maps a core error code to its bridge error code.</summary>
        public static string ToBridgeCode(RenderErrorCode code)
        {
            switch (code)
            {
                case RenderErrorCode.InvalidRequest: return BridgeErrorCodes.InvalidRequest;
                case RenderErrorCode.AuthFailed: return BridgeErrorCodes.AuthFailed;
                case RenderErrorCode.InsufficientCredits: return BridgeErrorCodes.InsufficientCredits;
                case RenderErrorCode.RateLimited: return BridgeErrorCodes.RateLimited;
                case RenderErrorCode.ContentBlocked: return BridgeErrorCodes.ContentBlocked;
                case RenderErrorCode.ModelUnavailable: return BridgeErrorCodes.ModelUnavailable;
                case RenderErrorCode.Timeout: return BridgeErrorCodes.Timeout;
                case RenderErrorCode.Network: return BridgeErrorCodes.Network;
                case RenderErrorCode.ProviderError: return BridgeErrorCodes.ProviderError;
                case RenderErrorCode.NoImageReturned: return BridgeErrorCodes.NoImage;
                default: return BridgeErrorCodes.Internal;
            }
        }

        /// <summary>Converts a generation failure.</summary>
        public static RpcError FromRenderException(RenderException exception)
        {
            return new RpcError
            {
                Code = ToBridgeCode(exception.Code),
                Message = exception.Message,
                Detail = exception.ProviderMessage,
                Retryable = exception.IsRetryable
            };
        }

        /// <summary>Converts any exception thrown while handling a call.</summary>
        public static RpcError FromException(Exception exception)
        {
            switch (exception)
            {
                case RenderException render:
                    return FromRenderException(render);
                case BridgeException bridge:
                    return new RpcError { Code = bridge.Code, Message = bridge.Message, Detail = bridge.Detail };
                case OperationCanceledException _:
                    return new RpcError { Code = BridgeErrorCodes.Cancelled, Message = "The operation was cancelled." };
                case RhinoHostException rhino:
                    return new RpcError { Code = BridgeErrorCodes.RhinoError, Message = rhino.Message, Detail = rhino.Message };
                case KeyNotFoundException notFound:
                    return new RpcError { Code = BridgeErrorCodes.NotFound, Message = notFound.Message };
                case JsonException json:
                    return new RpcError { Code = BridgeErrorCodes.InvalidRequest, Message = "Malformed parameters: " + json.Message };
                default:
                    // Type and message only: stack traces stay in the host log.
                    return new RpcError
                    {
                        Code = BridgeErrorCodes.Internal,
                        Message = "Unexpected error.",
                        Detail = exception.GetType().Name + ": " + exception.Message
                    };
            }
        }
    }
}
