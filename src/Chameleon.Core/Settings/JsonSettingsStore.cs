using System;
using System.Globalization;
using System.IO;
using System.Text;
using Chameleon.Core.Serialization;

namespace Chameleon.Core.Settings
{
    /// <summary>Stores settings as a JSON file, written atomically.</summary>
    public sealed class JsonSettingsStore : ISettingsStore
    {
        private readonly string _filePath;
        private readonly Func<AppSettings> _createDefaults;
        private readonly object _gate = new object();
        private string? _cachedJson;

        /// <summary>Creates a store.</summary>
        /// <param name="filePath">Full path of settings.json.</param>
        /// <param name="createDefaults">Produces the settings used when the file is missing or unreadable.</param>
        public JsonSettingsStore(string filePath, Func<AppSettings> createDefaults)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _createDefaults = createDefaults ?? throw new ArgumentNullException(nameof(createDefaults));
        }

        /// <inheritdoc />
        public event EventHandler? Changed;

        /// <inheritdoc />
        public bool Exists => File.Exists(_filePath);

        /// <inheritdoc />
        public AppSettings Load()
        {
            lock (_gate)
            {
                if (_cachedJson == null)
                {
                    _cachedJson = CoreJson.Serialize(ReadFromDisk());
                }

                // Deserializing the cached text hands every caller an independent copy.
                return CoreJson.Deserialize<AppSettings>(_cachedJson) ?? _createDefaults();
            }
        }

        /// <inheritdoc />
        public void Save(AppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
            string json = CoreJson.Serialize(settings);

            lock (_gate)
            {
                string directory = Path.GetDirectoryName(_filePath) ?? ".";
                Directory.CreateDirectory(directory);

                // Write next to the target, then swap: a crash never leaves a half-written file.
                string tempPath = _filePath + ".tmp";
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));
                if (File.Exists(_filePath))
                {
                    File.Replace(tempPath, _filePath, null);
                }
                else
                {
                    File.Move(tempPath, _filePath);
                }

                _cachedJson = json;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        private AppSettings ReadFromDisk()
        {
            if (!File.Exists(_filePath))
            {
                return _createDefaults();
            }

            try
            {
                AppSettings? settings = CoreJson.Deserialize<AppSettings>(File.ReadAllText(_filePath, Encoding.UTF8));
                if (settings != null)
                {
                    return settings;
                }
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // Fall through: keep the damaged file for inspection and start from defaults.
            }
            catch (IOException)
            {
                return _createDefaults();
            }

            QuarantineCorruptFile();
            return _createDefaults();
        }

        private void QuarantineCorruptFile()
        {
            try
            {
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
                string directory = Path.GetDirectoryName(_filePath) ?? ".";
                string name = Path.GetFileNameWithoutExtension(_filePath) + ".corrupt-" + stamp + Path.GetExtension(_filePath);
                File.Move(_filePath, Path.Combine(directory, name));
            }
            catch (IOException)
            {
                // Quarantine is best effort; the next Save overwrites the file anyway.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
