using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Backends;
using Chameleon.Core.Generation;
using Chameleon.Core.Imaging;
using Chameleon.Core.Providers;

namespace Chameleon.Cli
{
    /// <summary>Runs one real generation and writes the resulting image(s) to disk.</summary>
    internal sealed class GenerateCommand : ICliCommand
    {
        public string Name => "generate";

        public string Summary => "Generate an image from a source image and a prompt.";

        public async Task<int> RunAsync(CliContext context, ArgumentReader args, CancellationToken cancellationToken)
        {
            ProviderProfile profile = context.ResolveProvider(args);
            DirectBackend backend = context.CreateBackend(profile);

            var request = new GenerationRequest
            {
                ModelId = args.Get("model") ?? profile.DefaultModel,
                Prompt = args.Require("prompt"),
                SystemPrompt = args.Get("system") ?? context.Catalog.DefaultSystemPrompt,
                ImageSize = args.Get("size") ?? context.Catalog.DefaultImageSize,
                AspectRatio = args.Get("aspect")
            };

            string? source = args.Get("source");
            if (source != null)
            {
                request.Source = LoadImage(source);
            }

            string? mask = args.Get("mask");
            if (mask != null)
            {
                request.Mask = LoadImage(mask);
            }

            foreach (string reference in args.GetAll("ref"))
            {
                request.References.Add(ParseReference(reference));
            }

            string output = args.Get("out") ?? "out.png";
            var stopwatch = Stopwatch.StartNew();
            GenerationResult result;
            try
            {
                var progress = new Progress<GenerationProgress>(p => context.Error.WriteLine("[" + p.Stage + "] " + p.Message));
                result = await backend.GenerateAsync(request, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (RenderException ex)
            {
                context.ReportFailure(ex);
                return 1;
            }

            for (int i = 0; i < result.Images.Count; i++)
            {
                ImageData image = result.Images[i];
                string path = OutputPath(output, i, image.FileExtension);
                File.WriteAllBytes(path, image.Bytes);
                context.Output.WriteLine(path + "  " + image.Width + "x" + image.Height + "  " + image.MimeType);
            }

            if (!string.IsNullOrEmpty(result.Text))
            {
                context.Output.WriteLine("Model text: " + result.Text);
            }

            context.Output.WriteLine("Done in " + stopwatch.Elapsed.TotalSeconds.ToString("0.0") + " s.");
            return 0;
        }

        private static ImageData LoadImage(string path)
        {
            if (!File.Exists(path))
            {
                throw new CliUsageException("File not found: " + path);
            }

            return ImageData.TryFromBytes(File.ReadAllBytes(path))
                   ?? throw new CliUsageException("Not a PNG, JPEG or WebP image: " + path);
        }

        // "--ref path" or "--ref path:role" where role is style, material, object or consistency.
        private static ReferenceImage ParseReference(string value)
        {
            ReferenceRole role = ReferenceRole.Style;
            string path = value;

            int colon = value.LastIndexOf(':');
            if (colon > 1 && Enum.TryParse(value.Substring(colon + 1), true, out ReferenceRole parsed))
            {
                role = parsed;
                path = value.Substring(0, colon);
            }

            return new ReferenceImage(LoadImage(path), role);
        }

        // The file gets the extension of what the provider actually returned.
        private static string OutputPath(string requested, int index, string extension)
        {
            string directory = Path.GetDirectoryName(requested) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(requested);
            string suffix = index == 0 ? string.Empty : "-" + (index + 1);
            return Path.Combine(directory, name + suffix + "." + extension);
        }
    }
}
