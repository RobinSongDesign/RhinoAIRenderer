using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Backends;
using Chameleon.Core.Generation;
using Chameleon.Core.Providers;

namespace Chameleon.Cli
{
    /// <summary>Checks that the API key is accepted, without generating an image.</summary>
    internal sealed class ValidateCommand : ICliCommand
    {
        public string Name => "validate";

        public string Summary => "Check the API key against a provider.";

        public async Task<int> RunAsync(CliContext context, ArgumentReader args, CancellationToken cancellationToken)
        {
            ProviderProfile profile = context.ResolveProvider(args);
            DirectBackend backend = context.CreateBackend(profile);
            string model = args.Get("model") ?? profile.DefaultModel;

            try
            {
                await backend.ValidateAsync(model, cancellationToken).ConfigureAwait(false);
            }
            catch (RenderException ex)
            {
                context.ReportFailure(ex);
                return 1;
            }

            context.Output.WriteLine("OK: the key is accepted by " + profile.DisplayName + " (" + model + ").");
            return 0;
        }
    }
}
