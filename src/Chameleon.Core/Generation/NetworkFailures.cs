using System;
using System.Net.Sockets;

namespace Chameleon.Core.Generation
{
    /// <summary>
    /// Turns transport exceptions into user-facing failures. It tells "this computer refuses to let
    /// the program connect" (a firewall rule or security software blocking Rhino) apart from an
    /// unreachable server: the first is fixed on the user's machine, the second is not.
    /// </summary>
    public static class NetworkFailures
    {
        /// <summary>
        /// True when the operating system refused the connection (WSAEACCES, error 10013 on Windows).
        /// .NET wraps it differently per runtime (HttpRequestException → SocketException on .NET 7,
        /// HttpRequestException → WebException → SocketException on .NET Framework), hence the walk.
        /// </summary>
        public static bool IsBlockedBySystem(Exception? exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is SocketException socket && socket.SocketErrorCode == SocketError.AccessDenied)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Creates the failure for a transport exception.</summary>
        /// <param name="exception">The HttpRequestException or IOException caught.</param>
        /// <param name="detail">The exception's message, already redacted by the caller.</param>
        /// <param name="target">What could not be reached, for the message ("the provider").</param>
        public static RenderException From(Exception exception, string? detail, string target = "the provider")
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            return IsBlockedBySystem(exception)
                ? new RenderException(RenderErrorCode.NetworkBlocked, "This computer does not let Rhino connect to the internet (firewall or security software).", null, detail, exception)
                : new RenderException(RenderErrorCode.Network, "Could not reach " + target + ".", null, detail, exception);
        }
    }
}
