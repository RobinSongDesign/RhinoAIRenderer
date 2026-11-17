using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using Chameleon.Core.Backends;
using Chameleon.Core.Catalog;
using Chameleon.Core.Generation;
using Chameleon.Core.Prompts;
using Chameleon.Core.Providers;
using Chameleon.Core.Settings;

namespace Chameleon.Cli
{
    /// <summary>Shared services of one CLI invocation: catalog, output streams and backend creation.</summary>
    internal sealed class CliContext
    {
        /// <summary>Environment variable the API key is read from. Keys are never accepted as arguments.</summary>
        public const string ApiKeyVariable = "CHAMELEON_API_KEY";

        private const string AdHocProviderId = "cli-custom";

        public CliContext(TextWriter output, TextWriter error, Func<string, string?> getEnvironmentVariable, HttpClient httpClient)
        {
            Output = output;
            Error = error;
            GetEnvironmentVariable = getEnvironmentVariable;
            HttpClient = httpClient;
            Catalog = CatalogLoader.LoadBuiltIn();
        }

        public TextWriter Output { get; }

        public TextWriter Error { get; }

        public Func<string, string?> GetEnvironmentVariable { get; }

        public HttpClient HttpClient { get; }

        public ChameleonCatalog Catalog { get; }

        /// <summary>
        /// Resolves the provider named by --provider, or builds an ad-hoc one from
        /// --base-url / --format / --auth / --model.
        /// </summary>
        public ProviderProfile ResolveProvider(ArgumentReader args)
        {
            string? baseUrl = args.Get("base-url");
            if (baseUrl != null)
            {
                string format = args.Get("format") ?? ApiFormats.Gemini;
                string model = args.Require("model");
                return new ProviderProfile
                {
                    Id = AdHocProviderId,
                    DisplayName = baseUrl,
                    BaseUrl = baseUrl.TrimEnd('/'),
                    ApiFormat = format,
                    AuthType = args.Get("auth") ?? AuthTypes.Bearer,
                    DefaultModel = model,
                    Models = { Catalog.ResolveModel(format, model) }
                };
            }

            string providerId = args.Get("provider") ?? Catalog.DefaultProviderId;
            ProviderProfile? profile = new ProviderRegistry(Catalog)
                .Build(null, null, Languages.English)
                .FirstOrDefault(p => string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase));

            return profile ?? throw new CliUsageException(
                "Unknown provider '" + providerId + "'. Run the 'providers' command to list the built-in ones.");
        }

        /// <summary>Creates a backend for the provider using the key from the environment.</summary>
        public DirectBackend CreateBackend(ProviderProfile profile)
        {
            string? apiKey = GetEnvironmentVariable(ApiKeyVariable);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new CliUsageException(
                    "No API key. Set the " + ApiKeyVariable + " environment variable; keys are not accepted on the command line.");
            }

            var secrets = new InMemorySecretStore();
            secrets.Set(SecretNames.ForProvider(profile.Id), apiKey!);
            var factory = new DirectBackendFactory(AdapterRegistry.CreateDefault(), HttpClient, new PromptComposer());
            return factory.Create(profile, secrets);
        }

        /// <summary>Prints a failure the same way for every command.</summary>
        public void ReportFailure(RenderException ex)
        {
            Error.WriteLine("Failed: " + ex.Code);
            Error.WriteLine("  " + ex.Message);
            if (ex.HttpStatus.HasValue)
            {
                Error.WriteLine("  HTTP status: " + ex.HttpStatus.Value);
            }

            if (!string.IsNullOrEmpty(ex.ProviderMessage))
            {
                Error.WriteLine("  Provider said: " + ex.ProviderMessage);
            }
        }
    }
}
