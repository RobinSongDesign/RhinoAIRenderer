using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Backends;

namespace Chameleon.Cli
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                // Let the in-flight request unwind instead of killing the process mid-write.
                e.Cancel = true;
                cancellation.Cancel();
            };

            using HttpClient httpClient = DirectBackendFactory.CreateSharedHttpClient();
            var context = new CliContext(Console.Out, Console.Error, Environment.GetEnvironmentVariable, httpClient);
            var application = new CliApplication(new ICliCommand[]
            {
                new ProvidersCommand(),
                new ValidateCommand(),
                new GenerateCommand()
            });

            return await application.RunAsync(context, args, cancellation.Token).ConfigureAwait(false);
        }
    }
}
