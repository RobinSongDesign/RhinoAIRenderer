using System;
using System.IO;
using Chameleon.Core.Imaging;

namespace Chameleon.App.Assets
{
    /// <summary>
    /// Asset store backed by a directory: one file per image, named by content hash. Meant for
    /// disposable working data (captures, uploads, results); stale files are purged on start.
    /// </summary>
    public sealed class DiskAssetStore : IAssetStore
    {
        /// <summary>Files not read or written for this long are removed by <see cref="PurgeStale"/>.</summary>
        public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(7);

        private static readonly string[] Extensions = { "png", "jpg", "webp" };

        private readonly string _directory;

        /// <summary>Creates a store over a directory, which is created when missing.</summary>
        public DiskAssetStore(string directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            Directory.CreateDirectory(_directory);
        }

        /// <inheritdoc />
        public AssetRef Put(ImageData image)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            string path = PathFor(image.Sha256Hex, image.FileExtension);
            if (File.Exists(path))
            {
                Touch(path);
            }
            else
            {
                // Write to a temporary name first so a concurrent reader never sees a partial file.
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temp, image.Bytes);
                try
                {
                    File.Move(temp, path);
                }
                catch (IOException)
                {
                    // Another thread stored the same content first; ours is redundant.
                    File.Delete(temp);
                }
            }

            return AssetIds.Describe(image);
        }

        /// <inheritdoc />
        public ImageData? TryGet(string assetId)
        {
            if (!AssetIds.IsValid(assetId))
            {
                return null;
            }

            foreach (string extension in Extensions)
            {
                string path = PathFor(assetId, extension);
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    ImageData? image = ImageData.TryFromBytes(File.ReadAllBytes(path));
                    if (image != null)
                    {
                        Touch(path);
                    }

                    return image;
                }
                catch (IOException)
                {
                    return null;
                }
            }

            return null;
        }

        /// <summary>True when an image with this id is stored.</summary>
        public bool Contains(string assetId)
        {
            return AssetIds.IsValid(assetId) && Array.Exists(Extensions, e => File.Exists(PathFor(assetId, e)));
        }

        /// <summary>Deletes an image; unknown ids are ignored.</summary>
        public void Delete(string assetId)
        {
            if (!AssetIds.IsValid(assetId))
            {
                return;
            }

            foreach (string extension in Extensions)
            {
                try
                {
                    File.Delete(PathFor(assetId, extension));
                }
                catch (IOException)
                {
                    // In use; an unreferenced file left behind is harmless.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        /// <summary>Deletes files that have not been used for longer than <paramref name="maxAge"/>.</summary>
        /// <returns>Number of files removed.</returns>
        public int PurgeStale(TimeSpan maxAge)
        {
            int removed = 0;
            DateTime cutoff = DateTime.UtcNow - maxAge;
            foreach (string file in Directory.GetFiles(_directory))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                        removed++;
                    }
                }
                catch (IOException)
                {
                    // In use or already gone; the next start will try again.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return removed;
        }

        private string PathFor(string assetId, string extension)
        {
            return Path.Combine(_directory, assetId + "." + extension);
        }

        // The write time doubles as "last used", which is what the purge goes by.
        private static void Touch(string path)
        {
            try
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
