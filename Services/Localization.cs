using System.Collections.Generic;

namespace AIRenderer.Services
{
    public enum Language
    {
        Chinese,
        English
    }

    public static class Loc
    {
        private static Language _currentLanguage = Language.Chinese;

        public static Language CurrentLanguage
        {
            get => _currentLanguage;
            set => _currentLanguage = value;
        }

        private static readonly Dictionary<string, Dictionary<Language, string>> _translations = new Dictionary<string, Dictionary<Language, string>>
        {
            // Main Window
            { "SETTINGS", new Dictionary<Language, string> { { Language.Chinese, "设置" }, { Language.English, "SETTINGS" } } },
            { "1. CAPTURE", new Dictionary<Language, string> { { Language.Chinese, "1. 截图" }, { Language.English, "1. CAPTURE" } } },
            { "2. SERVICE PROVIDER", new Dictionary<Language, string> { { Language.Chinese, "2. 服务商" }, { Language.English, "2. SERVICE PROVIDER" } } },
            { "3. GENERATION PARAMETERS", new Dictionary<Language, string> { { Language.Chinese, "3. 生成参数" }, { Language.English, "3. GENERATION PARAMETERS" } } },

            // Buttons
            { "Capture Active View", new Dictionary<Language, string> { { Language.Chinese, "捕获当前视图" }, { Language.English, "Capture Active View" } } },
            { "Generate Image", new Dictionary<Language, string> { { Language.Chinese, "生成图片" }, { Language.English, "Generate Image" } } },
            { "Save Result", new Dictionary<Language, string> { { Language.Chinese, "保存结果" }, { Language.English, "Save Result" } } },
            { "Use as Source", new Dictionary<Language, string> { { Language.Chinese, "用作源图" }, { Language.English, "Use as Source" } } },
            { "Clear All", new Dictionary<Language, string> { { Language.Chinese, "清除全部" }, { Language.English, "Clear All" } } },

            // Labels
            { "Model", new Dictionary<Language, string> { { Language.Chinese, "模型" }, { Language.English, "Model" } } },
            { "Style Template", new Dictionary<Language, string> { { Language.Chinese, "风格模板" }, { Language.English, "Style Template" } } },
            { "Prompt", new Dictionary<Language, string> { { Language.Chinese, "提示词" }, { Language.English, "Prompt" } } },
            { "System Prompt", new Dictionary<Language, string> { { Language.Chinese, "系统提示词" }, { Language.English, "System Prompt" } } },
            { "Aspect Ratio", new Dictionary<Language, string> { { Language.Chinese, "宽高比" }, { Language.English, "Aspect Ratio" } } },
            { "Image Size", new Dictionary<Language, string> { { Language.Chinese, "图片尺寸" }, { Language.English, "Image Size" } } },
            { "Provider:", new Dictionary<Language, string> { { Language.Chinese, "服务商：" }, { Language.English, "Provider: " } } },

            // Placeholders / Hints
            { "Click 'Capture View' to capture viewport", new Dictionary<Language, string> { { Language.Chinese, "点击「捕获视图」来截取视口" }, { Language.English, "Click 'Capture View' to capture viewport" } } },
            { "Generated image will appear here", new Dictionary<Language, string> { { Language.Chinese, "生成的图片将显示在这里" }, { Language.English, "Generated image will appear here" } } },
            { "Click gear icon above to configure API settings", new Dictionary<Language, string> { { Language.Chinese, "点击上方齿轮图标配置API设置" }, { Language.English, "Click gear icon above to configure API settings" } } },

            // Settings Window
            { "API Settings", new Dictionary<Language, string> { { Language.Chinese, "API设置" }, { Language.English, "API Settings" } } },
            { "Service Provider", new Dictionary<Language, string> { { Language.Chinese, "服务商" }, { Language.English, "Service Provider" } } },
            { "API Key", new Dictionary<Language, string> { { Language.Chinese, "API密钥" }, { Language.English, "API Key" } } },
            { "API URL", new Dictionary<Language, string> { { Language.Chinese, "API地址" }, { Language.English, "API URL" } } },
            { "Get API KEY", new Dictionary<Language, string> { { Language.Chinese, "获取API KEY" }, { Language.English, "Get API KEY" } } },
            { "Test", new Dictionary<Language, string> { { Language.Chinese, "测试" }, { Language.English, "Test" } } },
            { "Save", new Dictionary<Language, string> { { Language.Chinese, "保存" }, { Language.English, "Save" } } },
            { "Cancel", new Dictionary<Language, string> { { Language.Chinese, "取消" }, { Language.English, "Cancel" } } },
            { "Language", new Dictionary<Language, string> { { Language.Chinese, "语言" }, { Language.English, "Language" } } },

            // Test Results
            { "Please enter API Key", new Dictionary<Language, string> { { Language.Chinese, "请输入API密钥" }, { Language.English, "Please enter API Key" } } },
            { "Testing...", new Dictionary<Language, string> { { Language.Chinese, "测试中..." }, { Language.English, "Testing..." } } },
            { "API Key is valid!", new Dictionary<Language, string> { { Language.Chinese, "API密钥有效！" }, { Language.English, "API Key is valid!" } } },
            { "API Key is invalid", new Dictionary<Language, string> { { Language.Chinese, "API密钥无效" }, { Language.English, "API Key is invalid" } } },
            { "Connection failed", new Dictionary<Language, string> { { Language.Chinese, "连接失败" }, { Language.English, "Connection failed" } } },

            // Messages
            { "LanguageRestartHint", new Dictionary<Language, string> { { Language.Chinese, "语言切换将在重启后生效" }, { Language.English, "Language change will take effect after restart" } } },
            { "Notice", new Dictionary<Language, string> { { Language.Chinese, "提示" }, { Language.English, "Notice" } } },
        };

        public static string Get(string key)
        {
            if (_translations.TryGetValue(key, out var dict))
            {
                if (dict.TryGetValue(_currentLanguage, out var value))
                {
                    return value;
                }
            }
            return key;
        }

        public static string[] LanguageOptions => new[] { "中文", "English" };

        public static Language GetLanguageFromIndex(int index)
        {
            return index == 0 ? Language.Chinese : Language.English;
        }
    }
}
