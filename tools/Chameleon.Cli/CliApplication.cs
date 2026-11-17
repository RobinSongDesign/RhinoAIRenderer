using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Chameleon.Cli
{
    /// <summary>Dispatches the command line to a sub-command.</summary>
    internal sealed class CliApplication
    {
        private readonly IReadOnlyList<ICliCommand> _commands;

        public CliApplication(IReadOnlyList<ICliCommand> commands)
        {
            _commands = commands;
        }

        public async Task<int> RunAsync(CliContext context, string[] args, CancellationToken cancellationToken)
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h" || args[0] == "help")
            {
                PrintUsage(context);
                return args.Length == 0 ? 2 : 0;
            }

            ICliCommand? command = _commands.FirstOrDefault(c => string.Equals(c.Name, args[0], StringComparison.OrdinalIgnoreCase));
            if (command == null)
            {
                context.Error.WriteLine("Unknown command '" + args[0] + "'.");
                PrintUsage(context);
                return 2;
            }

            try
            {
                return await command.RunAsync(context, new ArgumentReader(args.Skip(1).ToArray()), cancellationToken).ConfigureAwait(false);
            }
            catch (CliUsageException ex)
            {
                context.Error.WriteLine(ex.Message);
                return 2;
            }
            catch (OperationCanceledException)
            {
                context.Error.WriteLine("Cancelled.");
                return 130;
            }
        }

        private void PrintUsage(CliContext context)
        {
            context.Error.WriteLine("chameleon-cli: call real image providers through Chameleon.Core.");
            context.Error.WriteLine();
            context.Error.WriteLine("Commands:");
            foreach (ICliCommand command in _commands)
            {
                context.Error.WriteLine("  " + command.Name.PadRight(10) + command.Summary);
            }

            context.Error.WriteLine();
            context.Error.WriteLine("Options (generate / validate):");
            context.Error.WriteLine("  --provider <id>      Built-in provider (see 'providers'); default is the catalog default.");
            context.Error.WriteLine("  --base-url <url>     Use a custom endpoint instead; requires --model.");
            context.Error.WriteLine("  --format <f>         gemini (default) or openai, for --base-url.");
            context.Error.WriteLine("  --auth <a>           bearer (default) or goog, for --base-url.");
            context.Error.WriteLine("  --model <id>         Model id; defaults to the provider's default model.");
            context.Error.WriteLine("Options (generate):");
            context.Error.WriteLine("  --prompt <text>      Required.");
            context.Error.WriteLine("  --source <file>      Source image (PNG, JPEG or WebP).");
            context.Error.WriteLine("  --ref <file[:role]>  Reference image; role is style, material, object or consistency. Repeatable.");
            context.Error.WriteLine("  --mask <file>        Mask image (white = editable), for models that support it.");
            context.Error.WriteLine("  --size <tier>        0.5K, 1K, 2K or 4K. Default 1K.");
            context.Error.WriteLine("  --aspect <w:h>       Aspect ratio; omit to keep the source ratio.");
            context.Error.WriteLine("  --system <text>      System prompt; defaults to the built-in one.");
            context.Error.WriteLine("  --out <file>         Output path. Default out.png.");
            context.Error.WriteLine();
            context.Error.WriteLine("The API key is read from the " + CliContext.ApiKeyVariable + " environment variable.");
        }
    }
}
