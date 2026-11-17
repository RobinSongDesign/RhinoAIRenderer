using Chameleon.Core.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chameleon.App.Bridge
{
    /// <summary>
    /// JSON conventions of the UI bridge: camelCase properties, enums as camelCase strings, nulls
    /// omitted. Shares its configuration with the core so both speak one dialect.
    /// </summary>
    public static class BridgeJson
    {
        private static readonly JsonSerializer Serializer = JsonSerializer.Create(CoreJson.Settings);

        /// <summary>Serializes a value to compact JSON.</summary>
        public static string Serialize(object? value)
        {
            return JsonConvert.SerializeObject(value, Formatting.None, CoreJson.Settings);
        }

        /// <summary>Converts a value into a JSON token.</summary>
        public static JToken ToToken(object? value)
        {
            return value == null ? JValue.CreateNull() : JToken.FromObject(value, Serializer);
        }

        /// <summary>Converts a JSON token into a typed object.</summary>
        /// <exception cref="JsonException">The token does not match the type.</exception>
        public static T? ToObject<T>(JToken token)
        {
            return token.ToObject<T>(Serializer);
        }

        /// <summary>Parses JSON text into a typed object.</summary>
        /// <exception cref="JsonException">The text is not valid JSON for the type.</exception>
        public static T? Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, CoreJson.Settings);
        }
    }
}
