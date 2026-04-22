using System.Collections.Generic;

namespace AIRenderer.Models
{
    /// <summary>
    /// API 服务商枚举（内置服务商）
    /// </summary>
    public enum ApiProvider
    {
        Gemini,
        BltAI
    }

    /// <summary>
    /// 用户自定义服务商配置
    /// </summary>
    public class CustomProviderConfig
    {
        public string Id { get; set; }
        public string DisplayName { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        /// <summary>"bearer" = Authorization: Bearer，"goog" = x-goog-api-key</summary>
        public string AuthType { get; set; } = "bearer";
        /// <summary>"gemini" = Gemini generateContent 格式，"openai" = OpenAI Images API 格式</summary>
        public string ApiFormat { get; set; } = "gemini";
        public List<string> Models { get; set; } = new List<string>();
        public string DefaultModel { get; set; } = "";
    }

    /// <summary>
    /// 统一的服务商条目，内置和自定义服务商均使用此类型
    /// </summary>
    public class ProviderItem
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string BaseUrl { get; set; }
        public List<string> Models { get; set; } = new List<string>();
        public string DefaultModel { get; set; }
        public bool IsCustom { get; set; }
        public string AuthType { get; set; } = "bearer";
        /// <summary>"gemini" = Gemini generateContent 格式，"openai" = OpenAI Images API 格式</summary>
        public string ApiFormat { get; set; } = "gemini";
        public string ApiKeyUrl { get; set; }
        public ApiProvider? BuiltInProvider { get; set; }

        public static ProviderItem FromBuiltIn(ApiProviderConfig config)
        {
            return new ProviderItem
            {
                Id = config.Provider.ToString(),
                DisplayName = config.DisplayName,
                BaseUrl = config.BaseUrl,
                Models = config.Models ?? new List<string>(),
                DefaultModel = config.DefaultModel,
                IsCustom = false,
                AuthType = config.Provider == ApiProvider.Gemini ? "goog" : "bearer",
                ApiKeyUrl = config.ApiKeyUrl,
                BuiltInProvider = config.Provider
            };
        }

        public static ProviderItem FromCustom(CustomProviderConfig config)
        {
            var models = config.Models ?? new List<string>();
            return new ProviderItem
            {
                Id = config.Id,
                DisplayName = config.DisplayName,
                BaseUrl = config.BaseUrl,
                Models = models,
                DefaultModel = !string.IsNullOrEmpty(config.DefaultModel)
                    ? config.DefaultModel
                    : (models.Count > 0 ? models[0] : ""),
                IsCustom = true,
                AuthType = config.AuthType ?? "bearer",
                ApiFormat = config.ApiFormat ?? "gemini",
                ApiKeyUrl = null,
                BuiltInProvider = null
            };
        }
    }

    /// <summary>
    /// API 服务商配置
    /// </summary>
    public class ApiProviderConfig
    {
        public ApiProvider Provider { get; set; }
        public string DisplayName { get; set; }
        public string BaseUrl { get; set; }
        public List<string> Models { get; set; }
        public string DefaultModel { get; set; }
        public Dictionary<string, string> ModelDisplayNames { get; set; }
        public string ApiKeyUrl { get; set; }

        public static ApiProviderConfig GetConfig(ApiProvider provider)
        {
            switch (provider)
            {
                case ApiProvider.Gemini:
                    return new ApiProviderConfig
                    {
                        Provider = ApiProvider.Gemini,
                        DisplayName = "Google原生",
                        BaseUrl = "https://generativelanguage.googleapis.com",
                        DefaultModel = "gemini-3.1-flash-image-preview",
                        Models = new List<string>
                        {
                            "gemini-3.1-flash-image-preview",
                            "gemini-3-pro-image-preview",
                            "gemini-2.5-flash-image"
                        },
                        ModelDisplayNames = new Dictionary<string, string>
                        {
                            { "gemini-3.1-flash-image-preview", "Nano Banana 2" },
                            { "gemini-3-pro-image-preview", "Nano Banana Pro" },
                            { "gemini-2.5-flash-image", "Nano Banana" }
                        },
                        ApiKeyUrl = "https://aistudio.google.com/app/apikey"
                    };
                case ApiProvider.BltAI:
                    return new ApiProviderConfig
                    {
                        Provider = ApiProvider.BltAI,
                        DisplayName = "柏拉图AI",
                        BaseUrl = "https://hk-api.gptbest.vip",
                        DefaultModel = "gemini-3.1-flash-image-preview",
                        Models = new List<string>
                        {
                            "gemini-3.1-flash-image-preview",
                            "gemini-3-pro-image-preview"
                        },
                        ModelDisplayNames = new Dictionary<string, string>
                        {
                            { "gemini-3.1-flash-image-preview", "Nano Banana 2" },
                            { "gemini-3-pro-image-preview", "Nano Banana Pro" }
                        },
                        ApiKeyUrl = "https://api.bltcy.ai/register?aff=2Z1d103040/"
                    };
                default:
                    return null;
            }
        }

        public static List<ApiProviderConfig> GetAllProviders()
        {
            return new List<ApiProviderConfig>
            {
                GetConfig(ApiProvider.Gemini),
                GetConfig(ApiProvider.BltAI)
            };
        }

        public static List<ProviderItem> GetAllProviderItems()
        {
            return new List<ProviderItem>
            {
                ProviderItem.FromBuiltIn(GetConfig(ApiProvider.Gemini)),
                ProviderItem.FromBuiltIn(GetConfig(ApiProvider.BltAI))
            };
        }
    }
}
