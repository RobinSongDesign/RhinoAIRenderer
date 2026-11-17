using System;

namespace Chameleon.Cli
{
    /// <summary>The command line is wrong; the message is shown to the user followed by the usage text.</summary>
    internal sealed class CliUsageException : Exception
    {
        public CliUsageException(string message)
            : base(message)
        {
        }
    }
}
