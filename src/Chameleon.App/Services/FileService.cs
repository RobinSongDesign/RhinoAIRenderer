using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Assets;
using Chameleon.App.Bridge;
using Chameleon.App.Bridge.Dto;
using Chameleon.App.Hosting;
using Chameleon.Core.Imaging;

namespace Chameleon.App.Services
{
    /// <summary>Saving images to disk and importing images, through the host's native dialogs.</summary>
    public sealed class FileService
    {
        private static readonly string[] ImportExtensions = { "png", "jpg", "jpeg", "webp" };

        private readonly IRhinoHost _host;
        private readonly IAssetStore _assets;

        /// <summary>Creates the service.</summary>
        public FileService(IRhinoHost host, IAssetStore assets)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        }

        /// <summary>Registers this service's RPC methods.</summary>
        public void Register(RpcRouter router)
        {
            router.Register<SaveImageParams, object?>("files.saveImage", async (p, ct) =>
            {
                string? path = await SaveImageAsync(p).ConfigureAwait(false);
                return path == null ? null : new { path };
            });
            router.Register<SaveManyParams, object?>("files.saveMany", async (p, ct) => await SaveManyAsync(p).ConfigureAwait(false));
            router.Register<PickImagesParams, IReadOnlyList<AssetRef>>("files.pickImages", (p, ct) => PickImagesAsync(p.Multiple));
        }

        /// <summary>Asks where to save one image and writes it.</summary>
        /// <returns>The written path, or null when the user cancelled.</returns>
        public async Task<string?> SaveImageAsync(SaveImageParams parameters)
        {
            ImageData image = RequireAsset(parameters.AssetId);
            string name = SanitizeFileName(parameters.SuggestedName);
            if (name.Length == 0)
            {
                name = "Chameleon";
            }

            string? path = await _host.ShowSaveFileDialogAsync(new SaveFileRequest
            {
                SuggestedFileName = name + "." + image.FileExtension,
                Extension = image.FileExtension
            }).ConfigureAwait(false);

            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            // The file content decides the extension, whatever the user typed.
            if (!string.Equals(Path.GetExtension(path).TrimStart('.'), image.FileExtension, StringComparison.OrdinalIgnoreCase))
            {
                path += "." + image.FileExtension;
            }

            File.WriteAllBytes(path!, image.Bytes);
            return path;
        }

        /// <summary>Asks for a folder and writes several images into it.</summary>
        /// <returns>The folder and the number of files written, or null when the user cancelled.</returns>
        public async Task<object?> SaveManyAsync(SaveManyParams parameters)
        {
            if (parameters.Items == null || parameters.Items.Count == 0)
            {
                throw BridgeException.InvalidRequest("Nothing to save.");
            }

            // Resolve everything before showing the dialog so a missing asset fails early.
            var images = parameters.Items.Select(i => new { i.FileName, Image = RequireAsset(i.AssetId) }).ToList();

            string? folder = await _host.ShowSelectFolderDialogAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(folder))
            {
                return null;
            }

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int saved = 0;
            foreach (var entry in images)
            {
                string baseName = SanitizeFileName(entry.FileName);
                if (baseName.Length == 0)
                {
                    baseName = "image";
                }

                // Two views can sanitize to the same name; never overwrite one with the other.
                string candidate = baseName;
                for (int suffix = 2; !usedNames.Add(candidate); suffix++)
                {
                    candidate = baseName + "-" + suffix;
                }

                File.WriteAllBytes(Path.Combine(folder!, candidate + "." + entry.Image.FileExtension), entry.Image.Bytes);
                saved++;
            }

            return new { folder, saved };
        }

        /// <summary>Lets the user pick image files and imports them as assets.</summary>
        public async Task<IReadOnlyList<AssetRef>> PickImagesAsync(bool multiple)
        {
            var request = new OpenFilesRequest { Multiple = multiple };
            foreach (string extension in ImportExtensions)
            {
                request.Extensions.Add(extension);
            }

            IReadOnlyList<string> paths = await _host.ShowOpenFilesDialogAsync(request).ConfigureAwait(false);
            var assets = new List<AssetRef>();
            foreach (string path in paths)
            {
                ImageData? image;
                try
                {
                    image = ImageData.TryFromBytes(File.ReadAllBytes(path));
                }
                catch (IOException)
                {
                    image = null;
                }

                // Files that are not PNG/JPEG/WebP are skipped; the UI reports when nothing came back.
                if (image != null)
                {
                    assets.Add(_assets.Put(image));
                }
            }

            return assets;
        }

        /// <summary>Removes characters that are not allowed in file names on either platform.</summary>
        public static string SanitizeFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            // Fixed set rather than Path.GetInvalidFileNameChars(), which differs per OS.
            const string invalid = "<>:\"/\\|?*";
            var chars = name!.Where(c => c >= ' ' && invalid.IndexOf(c) < 0).ToArray();
            return new string(chars).Trim().TrimEnd('.');
        }

        private ImageData RequireAsset(string assetId)
        {
            return _assets.TryGet(assetId) ?? throw BridgeException.NotFound("Unknown asset '" + assetId + "'.");
        }
    }
}
