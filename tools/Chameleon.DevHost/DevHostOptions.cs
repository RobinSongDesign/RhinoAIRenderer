using System;
using System.Collections.Generic;
using System.IO;

namespace Chameleon.DevHost
{
    /// <summary>Command-line options of the development host.</summary>
    public sealed class DevHostOptions
    {
        /// <summary>Port to listen on.</summary>
        public int Port { get; private set; } = 5199;

        /// <summary>Path token; "dev" by default so the URL is stable.</summary>
        public string Token { get; private set; } = "dev";

        /// <summary>Directory with the built front end; null serves a placeholder page.</summary>
        public string? UiDirectory { get; private set; }

        /// <summary>Directory for settings, cache and secrets; a fresh temporary folder by default.</summary>
        public string DataDirectory { get; private set; } = Path.Combine(Path.GetTempPath(), "chameleon-devhost-" + Guid.NewGuid().ToString("N"));

        /// <summary>Folder the fake save dialogs write into; defaults to "out" inside the data directory.</summary>
        public string? OutputDirectory { get; private set; }

        /// <summary>Browser origin allowed to call the bridge cross-origin (the Vite dev server).</summary>
        public string? AllowedOrigin { get; private set; }

        /// <summary>How long a fake generation takes.</summary>
        public TimeSpan Delay { get; private set; } = TimeSpan.FromMilliseconds(800);

        /// <summary>True to answer provider HTTP calls locally instead of using the network.</summary>
        public bool FakeProviders { get; private set; }

        /// <summary>Usage text.</summary>
        public const string Usage =
            "Usage: Chameleon.DevHost [options]\n" +
            "  --port <n>            Port to listen on (default 5199).\n" +
            "  --token <text>        Path token (default \"dev\").\n" +
            "  --ui <dir>            Built front end to serve (default: placeholder page).\n" +
            "  --data <dir>          Settings/cache/secrets folder (default: new temp folder).\n" +
            "  --out <dir>           Folder the fake save dialogs write into (default: <data>/out).\n" +
            "  --allow-origin <url>  Extra browser origin allowed to call the bridge (Vite dev server).\n" +
            "  --delay <ms>          Duration of a fake generation (default 800).\n" +
            "  --fake-providers      Answer provider HTTP calls locally; the accepted key is \"dev-good-key\".\n";

        /// <summary>Parses the command line.</summary>
        /// <exception cref="ArgumentException">An option is unknown or malformed.</exception>
        public static DevHostOptions Parse(IReadOnlyList<string> args)
        {
            var options = new DevHostOptions();
            for (int i = 0; i < args.Count; i++)
            {
                string name = args[i];
                if (name == "--fake-providers")
                {
                    options.FakeProviders = true;
                    continue;
                }

                if (i + 1 >= args.Count)
                {
                    throw new ArgumentException("Option '" + name + "' needs a value.");
                }

                string value = args[++i];
                switch (name)
                {
                    case "--port":
                        options.Port = int.Parse(value);
                        break;
                    case "--token":
                        options.Token = value;
                        break;
                    case "--ui":
                        options.UiDirectory = value;
                        break;
                    case "--data":
                        options.DataDirectory = value;
                        break;
                    case "--out":
                        options.OutputDirectory = value;
                        break;
                    case "--allow-origin":
                        options.AllowedOrigin = value;
                        break;
                    case "--delay":
                        options.Delay = TimeSpan.FromMilliseconds(int.Parse(value));
                        break;
                    default:
                        throw new ArgumentException("Unknown option '" + name + "'.");
                }
            }

            return options;
        }
    }
}
