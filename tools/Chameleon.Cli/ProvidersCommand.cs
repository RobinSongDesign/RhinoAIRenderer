using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Catalog;

namespace Chameleon.Cli
{
    /// <summary>Lists the built-in providers and models. Needs neither a key nor network access.</summary>
    internal sealed class ProvidersCommand : ICliCommand
    {
        public string Name => "providers";

        public string Summary => "List built-in providers and their models.";

        public Task<int> RunAsync(CliContext context, ArgumentReader args, CancellationToken cancellationToken)
        {
            foreach (CatalogProvider provider in context.Catalog.Providers)
            {
                string marker = provider.Id == context.Catalog.DefaultProviderId ? " (default)" : string.Empty;
                context.Output.WriteLine(provider.Id + marker + "  " + provider.DisplayName.Get(Languages.English));
                context.Output.WriteLine("  url:    " + provider.BaseUrl);
                context.Output.WriteLine("  format: " + provider.ApiFormat + ", auth: " + provider.AuthType);
                foreach (CatalogModel model in provider.Models)
                {
                    string isDefault = model.Id == provider.DefaultModel ? " *" : string.Empty;
                    context.Output.WriteLine("  model:  " + model.Id + "  [" + model.DisplayName + "]" + isDefault);
                }
            }

            return Task.FromResult(0);
        }
    }
}
