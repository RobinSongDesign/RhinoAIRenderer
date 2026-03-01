using System.Collections.Generic;

namespace AIRenderer.Models
{
    /// <summary>
    /// API 服务商枚举
    /// </summary>
    public enum ApiProvider
    {
        Gemini,
        BltAI
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
    }
}
