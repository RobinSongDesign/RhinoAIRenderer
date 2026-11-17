using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Chameleon.Core.Serialization
{
    /// <summary>The single JSON configuration used for files written and read by the core.</summary>
    public static class CoreJson
    {
        /// <summary>camelCase properties, untouched dictionary keys, enums as strings, nulls omitted.</summary>
        public static JsonSerializerSettings Settings { get; } = CreateSettings();

        /// <summary>Serializes with <see cref="Settings"/>, indented for human-readable files.</summary>
        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, Formatting.Indented, Settings);
        }

        /// <summary>Deserializes with <see cref="Settings"/>.</summary>
        public static T? Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }

        private static JsonSerializerSettings CreateSettings()
        {
            var settings = new JsonSerializerSettings
            {
                // Dictionary keys are identifiers (provider ids, backend ids) and must survive unchanged.
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false }
                },
                NullValueHandling = NullValueHandling.Ignore,
                DateParseHandling = DateParseHandling.DateTimeOffset
            };
            settings.Converters.Add(new StringEnumConverter(new CamelCaseNamingStrategy()));
            return settings;
        }
    }
}
