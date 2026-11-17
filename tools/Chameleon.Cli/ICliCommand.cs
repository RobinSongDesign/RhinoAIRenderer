using System.Threading;
using System.Threading.Tasks;

namespace Chameleon.Cli
{
    /// <summary>One sub-command of the tool.</summary>
    internal interface ICliCommand
    {
        /// <summary>Name typed on the command line.</summary>
        string Name { get; }

        /// <summary>One-line description for the usage text.</summary>
        string Summary { get; }

        /// <summary>Runs the command and returns the process exit code.</summary>
        Task<int> RunAsync(CliContext context, ArgumentReader args, CancellationToken cancellationToken);
    }
}
