using System;

namespace Chameleon.App.Hosting
{
    /// <summary>
    /// A Rhino-side operation failed in a way the user can act on (no active document, capture
    /// failed, unknown named view). The message is shown to the user as the error detail.
    /// </summary>
    public sealed class RhinoHostException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public RhinoHostException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
