using System;
using System.Collections.Generic;

namespace AIRenderer.Services
{
    public enum Language { Chinese, English }

    public static class Loc
    {
        private static Language _currentLanguage = Language.Chinese;

        public static event Action LanguageChanged;

        public static Language CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage == value) return;
                _currentLanguage = value;
                LanguageChanged?.Invoke();
            }
        }

        private static readonly Dictionary<string, Dictionary<Language, string>> T =
            new Dictionary<string, Dictionary<Language, string>>
        {
            // ── Mode toggle ────────────────────────────────────────────────
            { "Single",         new Dictionary<Language, string> { { Language.Chinese, "单图" },      { Language.English, "Single" } } },
            { "Batch",          new Dictionary<Language, string> { { Language.Chinese, "批量" },      { Language.English, "Batch" } } },

            // ── Section headers ────────────────────────────────────────────
            { "SETTINGS",                  new Dictionary<Language, string> { { Language.Chinese, "设置" },           { Language.English, "SETTINGS" } } },
            { "1. CAPTURE",                new Dictionary<Language, string> { { Language.Chinese, "1. 截图" },        { Language.English, "1. CAPTURE" } } },
            { "1. BATCH VIEWS",            new Dictionary<Language, string> { { Language.Chinese, "1. 批量视图" },    { Language.English, "1. BATCH VIEWS" } } },
            { "2. SERVICE PROVIDER",       new Dictionary<Language, string> { { Language.Chinese, "2. 服务商" },      { Language.English, "2. SERVICE PROVIDER" } } },
            { "2. STYLE REFERENCE",        new Dictionary<Language, string> { { Language.Chinese, "2. 风格参考图" },  { Language.English, "2. STYLE REFERENCE" } } },
            { "3. GENERATION PARAMETERS",  new Dictionary<Language, string> { { Language.Chinese, "3. 生成参数" },    { Language.English, "3. GENERATION PARAMETERS" } } },

            // ── Labels ─────────────────────────────────────────────────────
            { "Model",          new Dictionary<Language, string> { { Language.Chinese, "模型" },       { Language.English, "Model" } } },
            { "Style Template", new Dictionary<Language, string> { { Language.Chinese, "风格模板" },   { Language.English, "Style Template" } } },
            { "Prompt",         new Dictionary<Language, string> { { Language.Chinese, "提示词" },     { Language.English, "Prompt" } } },
            { "Prompt (shared)",new Dictionary<Language, string> { { Language.Chinese, "提示词（所有视图共用）" }, { Language.English, "Prompt (shared for all views)" } } },
            { "Aspect Ratio",   new Dictionary<Language, string> { { Language.Chinese, "宽高比" },     { Language.English, "Aspect Ratio" } } },
            { "Image Size",     new Dictionary<Language, string> { { Language.Chinese, "图片尺寸" },   { Language.English, "Image Size" } } },
            { "Language",       new Dictionary<Language, string> { { Language.Chinese, "语言" },       { Language.English, "Language" } } },
            { "Provider:",      new Dictionary<Language, string> { { Language.Chinese, "服务商：" },   { Language.English, "Provider: " } } },

            // ── Buttons ─────────────────────────────────────────────────────
            { "Capture Active View",        new Dictionary<Language, string> { { Language.Chinese, "捕获当前视图" },      { Language.English, "Capture Active View" } } },
            { "Load & Capture Named Views", new Dictionary<Language, string> { { Language.Chinese, "加载并捕获命名视图" }, { Language.English, "Load & Capture Named Views" } } },
            { "Select All",                 new Dictionary<Language, string> { { Language.Chinese, "全选" },             { Language.English, "Select All" } } },
            { "Deselect All",               new Dictionary<Language, string> { { Language.Chinese, "取消全选" },         { Language.English, "Deselect All" } } },
            { "Start Batch Render",         new Dictionary<Language, string> { { Language.Chinese, "开始批量渲染" },      { Language.English, "Start Batch Render" } } },
            { "Cancel Render",              new Dictionary<Language, string> { { Language.Chinese, "取消渲染" },          { Language.English, "Cancel Render" } } },
            { "Generate Image",             new Dictionary<Language, string> { { Language.Chinese, "生成图片" },          { Language.English, "Generate Image" } } },
            { "Save All Results",           new Dictionary<Language, string> { { Language.Chinese, "保存全部结果" },      { Language.English, "Save All Results" } } },
            { "Save Result",                new Dictionary<Language, string> { { Language.Chinese, "保存结果" },          { Language.English, "Save Result" } } },
            { "Use as Source",              new Dictionary<Language, string> { { Language.Chinese, "用作源图" },          { Language.English, "Use as Source" } } },
            { "Clear All",                  new Dictionary<Language, string> { { Language.Chinese, "清除全部" },          { Language.English, "Clear All" } } },
            { "Refresh",                    new Dictionary<Language, string> { { Language.Chinese, "刷新" },             { Language.English, "Refresh" } } },
            { "Regenerate",                 new Dictionary<Language, string> { { Language.Chinese, "重新生成" },          { Language.English, "Regenerate" } } },
            { "Save",                       new Dictionary<Language, string> { { Language.Chinese, "保存" },             { Language.English, "Save" } } },
            { "Cancel",                     new Dictionary<Language, string> { { Language.Chinese, "取消" },             { Language.English, "Cancel" } } },
            { "Test",                       new Dictionary<Language, string> { { Language.Chinese, "测试" },             { Language.English, "Test" } } },
            { "Get API KEY",                new Dictionary<Language, string> { { Language.Chinese, "获取 API KEY" },     { Language.English, "Get API KEY" } } },

            // ── Placeholders / info ─────────────────────────────────────────
            { "Click 'Capture View' to capture viewport",
                new Dictionary<Language, string> {
                    { Language.Chinese, "点击「捕获当前视图」截取视口" },
                    { Language.English, "Click 'Capture View' to capture viewport" } } },
            { "Generated image will appear here",
                new Dictionary<Language, string> {
                    { Language.Chinese, "生成的图片将显示在这里" },
                    { Language.English, "Generated image will appear here" } } },
            { "Click gear icon above to configure API settings",
                new Dictionary<Language, string> {
                    { Language.Chinese, "点击上方齿轮图标配置 API 设置" },
                    { Language.English, "Click gear icon above to configure API settings" } } },
            { "Load named views to begin",
                new Dictionary<Language, string> {
                    { Language.Chinese, "点击右侧「加载并捕获命名视图」开始" },
                    { Language.English, "Click 'Load & Capture Named Views' to begin" } } },
            { "Not captured",
                new Dictionary<Language, string> {
                    { Language.Chinese, "未捕获" },
                    { Language.English, "Not captured" } } },
            { "Reference image hint",
                new Dictionary<Language, string> {
                    { Language.Chinese, "拖入 / 点击选择参考图" },
                    { Language.English, "Drag / click to add style reference" } } },
            { "Reference image sub",
                new Dictionary<Language, string> {
                    { Language.Chinese, "光照、材质、风格等将以此图为基准" },
                    { Language.English, "Lighting, materials and style will match this image" } } },
            { "Addon prompt hint",
                new Dictionary<Language, string> {
                    { Language.Chinese, "此视图的附加提示词（追加到共用 Prompt 之后）" },
                    { Language.English, "Per-view addon prompt (appended to shared prompt)" } } },

            // ── Settings window ─────────────────────────────────────────────
            { "API Settings",       new Dictionary<Language, string> { { Language.Chinese, "API 设置" },   { Language.English, "API Settings" } } },
            { "Service Provider",   new Dictionary<Language, string> { { Language.Chinese, "服务商" },     { Language.English, "Service Provider" } } },
            { "API Key",            new Dictionary<Language, string> { { Language.Chinese, "API 密钥" },   { Language.English, "API Key" } } },
            { "API URL",            new Dictionary<Language, string> { { Language.Chinese, "API 地址" },   { Language.English, "API URL" } } },
            { "Test with model",    new Dictionary<Language, string> { { Language.Chinese, "测试使用的模型" }, { Language.English, "Test with model" } } },

            // ── Status messages ─────────────────────────────────────────────
            { "Please enter API Key",   new Dictionary<Language, string> { { Language.Chinese, "请输入 API 密钥" },   { Language.English, "Please enter API Key" } } },
            { "Testing...",             new Dictionary<Language, string> { { Language.Chinese, "测试中..." },         { Language.English, "Testing..." } } },
            { "API Key is valid!",      new Dictionary<Language, string> { { Language.Chinese, "API 密钥有效！" },    { Language.English, "API Key is valid!" } } },
            { "API Key is invalid",     new Dictionary<Language, string> { { Language.Chinese, "API 密钥无效" },      { Language.English, "API Key is invalid" } } },
            { "Connection failed",      new Dictionary<Language, string> { { Language.Chinese, "连接失败" },           { Language.English, "Connection failed" } } },
            { "completed suffix",       new Dictionary<Language, string> { { Language.Chinese, " 已完成" }, { Language.English, " done" } } },
            { "Source Image",           new Dictionary<Language, string> { { Language.Chinese, "源图" },              { Language.English, "Source Image" } } },
            { "Generated Result",       new Dictionary<Language, string> { { Language.Chinese, "生成结果" },           { Language.English, "Generated Result" } } },
        };

        public static string Get(string key)
        {
            if (T.TryGetValue(key, out var dict) && dict.TryGetValue(_currentLanguage, out var val))
                return val;
            return key;
        }

        public static string[] LanguageOptions => new[] { "中文", "English" };

        public static Language GetLanguageFromIndex(int index)
            => index == 0 ? Language.Chinese : Language.English;
    }
}
