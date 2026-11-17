using System;
using System.IO;
using System.Threading;
using Chameleon.App;
using Chameleon.App.Platform;
using Chameleon.App.Server;

namespace Chameleon.DevHost
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            DevHostOptions options;
            try
            {
                options = DevHostOptions.Parse(args);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(DevHostOptions.Usage);
                return 2;
            }

            var samples = new SampleImages();
            string outputDirectory = options.OutputDirectory ?? Path.Combine(options.DataDirectory, "out");
            var host = new FakeRhinoHost(samples, outputDirectory, Console.Out);

            IStaticFileProvider staticFiles =
                options.UiDirectory != null && File.Exists(Path.Combine(options.UiDirectory, "index.html"))
                    ? new DirectoryFileProvider(options.UiDirectory)
                    : new PlaceholderFileProvider();

            var appOptions = new ChameleonAppOptions(host, AppPaths.UnderRoot(options.DataDirectory), staticFiles)
            {
                Port = options.Port,
                Token = options.Token,
                AllowedDevOrigin = options.AllowedOrigin,
                HttpMessageHandler = options.FakeProviders ? new FakeProviderHandler(samples, options.Delay) : null
            };
            appOptions.ExtraBackends.Add(new FakeBackend(samples, options.Delay));

            using (ChameleonApp app = ChameleonApp.Start(appOptions))
            using (var stop = new ManualResetEventSlim())
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    stop.Set();
                };
                AppDomain.CurrentDomain.ProcessExit += (sender, e) => stop.Set();

                Console.Out.WriteLine("Chameleon DevHost listening at " + app.BaseUrl);
                Console.Out.WriteLine("Data directory: " + options.DataDirectory);
                Console.Out.WriteLine("Output directory: " + outputDirectory);
                Console.Out.Flush();
                stop.Wait();
            }

            return 0;
        }
    }
}
