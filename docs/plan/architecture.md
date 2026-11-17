# 目标架构

本文件描述重构完成后的结构。任务文档里出现的类型名、目录名、接口签名都以这里为准。
接口签名在实现过程中需要调整时，先改这里再改代码。

## 1. 总览

```
┌───────────────────────── Rhino 进程（Windows / macOS）─────────────────────────┐
│                                                                                │
│  Chameleon.UI（TypeScript + React，运行在 Eto WebView 中）                     │
│     画布与像素操作 · 交互 · 界面状态 · 中英文                                  │
│        │  RPC（请求/响应） · 事件流 · 资源 URL        见 bridge-protocol.md    │
│        ▼                                                                       │
│  Chameleon.App（netstandard2.0）                                               │
│     本地回环服务器 · RPC 路由 · 应用服务 · 资源库 · 项目存储 · 平台密钥存储     │
│        │                                   ▲                                   │
│        ▼                                   │ IRhinoHost                        │
│  Chameleon.Core（netstandard2.0）          │                                   │
│     IRenderBackend ─┬─ DirectBackend ── IProviderAdapter（Gemini / OpenAI…）   │
│                     └─ CloudBackend（P4）                                      │
│     提示词组合 · 任务队列 · 批量计划 · 设置 · 服务商目录 · 错误模型             │
│                                            │                                   │
│  Chameleon.Rhino（net7.0;net48，.rhp）─────┘                                   │
│     插件类 · 命令 · Eto 窗口 · 视口截图 · 命名视图 · 对象蒙版 · 文档绑定        │
└────────────────────────────────────────────────────────────────────────────────┘
        │ 自带 Key：直连服务商                    │ 账号模式：HTTPS + JWT
        ▼                                         ▼
  Gemini / 中转站 / OpenAI 兼容接口        Chameleon Server（P4，独立私有仓库）
                                           登录 · 点数账本 · 定价 · 上游代理 · 管理后台
```

三条原则：

- **像素操作只在 UI 里做**。裁剪、合成、蒙版栅格化、缩放都用浏览器 Canvas 完成。.NET 侧把图片当作不透明的编码字节（PNG/JPEG/WebP），只解析文件头取尺寸。这样 Core 不需要任何图像库，也不依赖 `System.Drawing`。
- **Rhino 只出现在 `Chameleon.Rhino`**。其余程序集不引用 RhinoCommon，可以在 Linux 上编译、测试、用假宿主运行。
- **UI 不知道走哪种接入方式**。UI 只跟 `backendId` 和能力描述打交道，自带 Key 和账号模式在 Core 里是同一个接口的两个实现。

## 2. 仓库布局

P1-T01 完成后的目录结构：

```
Chameleon.sln
Directory.Build.props              # 公共属性：版本号、LangVersion、Nullable、公司信息
AGENTS.md                          # agent 工作守则
src/
  Chameleon.Core/                  # netstandard2.0，仅依赖 Newtonsoft.Json
    Imaging/                       # ImageData、ImageProbe
    Generation/                    # GenerationRequest/Result、IRenderBackend、RenderException
    Providers/                     # ProviderProfile、IProviderAdapter、GeminiAdapter、OpenAIImagesAdapter
    Backends/                      # DirectBackend、CloudBackend（P4）
    Prompts/                       # PromptComposer
    Jobs/                          # GenerationQueue、BatchPlanner
    Settings/                      # AppSettings、ISettingsStore、ISecretStore、迁移
    Catalog/                       # 内置目录（嵌入的 JSON）与读取
    Sessions/                      # EditSession、ImageNode、Recipe（P3）
  Chameleon.App/                   # netstandard2.0，依赖 Core
    Bridge/                        # RpcRouter、EventBus、DTO
    Server/                        # LocalServer（HttpListener）
    Services/                      # 各 RPC 处理器背后的应用服务
    Assets/                        # AssetStore
    Hosting/                       # IRhinoHost、IAppPaths、HostInfo
    Platform/                      # WindowsDpapiSecretStore、MacKeychainSecretStore、AppPaths
  Chameleon.Rhino/                 # net7.0;net48 → Chameleon.rhp，依赖 App 与 RhinoCommon
  Chameleon.UI/                    # Vite + React + TypeScript；构建产物嵌入 Chameleon.Rhino
tests/
  Chameleon.Core.Tests/            # net8.0，xUnit
  Chameleon.App.Tests/             # net8.0，xUnit
tools/
  Chameleon.DevHost/               # net8.0 控制台：真实 App + 假 Rhino 宿主 + 假后端，供 UI 开发和端到端测试
  Chameleon.Cli/                   # net8.0 控制台：用真实 Key 调真实接口的冒烟工具
spikes/
  ChameleonSpike/                  # P0 验证插件，P2 结束后可删
contracts/
  cloud-api.openapi.yaml           # 云端 API 契约（P4-T01）
legacy/                            # 现有 WPF 插件原样移入，P2-T12 删除
docs/plan/                         # 本计划
packaging/
  manifest.yml                     # Yak 清单（P2-T10）
```

服务端代码不放在本仓库（本仓库是公开的）。它位于独立的私有仓库，通过 git submodule 引入本仓库以复用 `Chameleon.Core` 的服务商适配器，详见 `phase-4-cloud.md`。

## 3. 分层与依赖规则

依赖方向只能自上而下，CI 里用项目引用关系保证：

| 程序集 | 可以引用 | 禁止 |
|---|---|---|
| `Chameleon.Core` | BCL、Newtonsoft.Json | RhinoCommon、Eto、`System.Drawing`、任何 UI 类型、`HttpListener` |
| `Chameleon.App` | Core、BCL | RhinoCommon、Eto、`System.Drawing` |
| `Chameleon.Rhino` | App、Core、RhinoCommon、Eto | WPF、WinForms、`Microsoft.Win32`、直接发起服务商 HTTP 请求 |
| `Chameleon.UI` | 只通过桥接协议访问宿主 | 直接请求服务商或云端接口、持有 API Key |
| `tests/*`、`tools/*` | Core、App | RhinoCommon |

补充规则：

- API Key、刷新令牌等机密只存在于 Core/App 进程内存和系统密钥库，不写入日志，不通过桥接协议发给 UI。UI 只能得到「是否已设置」。
- 所有异步方法接收并向下传递 `CancellationToken`。禁止 `.Result`、`.Wait()`、`async void`（事件处理器除外）。
- `HttpClient` 实例共享，但请求头只设在单个 `HttpRequestMessage` 上，不改 `DefaultRequestHeaders`。
- 面向用户的失败一律抛 `RenderException`（见第 7 节），不返回 `null` 表示失败。

## 4. 运行时约束

### 4.1 目标框架

| 项目 | 目标框架 | 原因 |
|---|---|---|
| Core、App | `netstandard2.0` | 同时被 net48、net7.0 的插件和 net8.0 的服务端、测试引用 |
| Rhino | `net7.0;net48` | Rhino 8 运行在 .NET 7（Windows 可切到 .NET Framework 4.8），Rhino 9 运行在 .NET 8，能加载 net7.0 程序集；macOS 只有 .NET 运行时 |
| tests、tools | `net8.0` | 本地和 CI 只需安装 .NET 8 SDK |

- RhinoCommon 引用版本固定为 `8.19.25132.1001`，带 `ExcludeAssets="runtime"`。这是第一个自带 `Eto.Forms.WebView.MessageReceived` 的版本（已对比各版本 NuGet 包内的 `Eto.xml` 确认：8.18 没有，8.19 有），因此最低支持 Rhino 8.19。
- 用 .NET 8 SDK 构建 `net7.0` 会有 `NETSDK1138` 提示，在 `Directory.Build.props` 里屏蔽。
- C# 语言版本 `10.0`，开启 `Nullable`。`netstandard2.0` 下不能直接用 `record` 和 `init`，需要时自行添加 `IsExternalInit` 垫片，否则用普通类。
- 已验证：在 Ubuntu 24.04 上 `apt-get install dotnet-sdk-8.0` 后，`netstandard2.0` 库、引用 RhinoCommon 的 `net7.0;net48` 插件工程（含 Eto WebView、`ViewCapture` 调用）、`net8.0` xUnit 测试都能构建并运行测试。现有的 WPF 工程在 Linux 上无法构建（缺 WindowsDesktop SDK），只能靠 Windows CI。

### 4.2 Rhino 线程

RhinoCommon 的文档访问、视口操作、截图必须在 Rhino UI 线程执行。本地服务器的请求在线程池线程上处理，所以 `IRhinoHost` 的实现内部统一用 `RhinoApp.InvokeOnUiThread` 切换，并用 `TaskCompletionSource` 把结果带回。调用方不关心线程。

### 4.3 WebView 引擎

| 平台 | 引擎 | 注意 |
|---|---|---|
| Windows | WebView2（Chromium） | 依赖系统的 WebView2 运行时，Windows 11 自带 |
| macOS | WKWebView（系统 WebKit） | 版本随 macOS，前端构建目标设为 `safari15`；键盘焦点、输入法、拖放需在 P0 实测 |

前端不使用只在新版 Chromium 中可用的特性。Vite 的 `build.target` 设为 `['chrome100', 'safari15']`。

### 4.4 文件位置

不使用 `Environment.SpecialFolder.ApplicationData` 推导 macOS 路径，因为 .NET 7 与 .NET 8 对它的解析不同，会导致 Rhino 8 和 Rhino 9 读到不同目录。由 `IAppPaths` 显式给出：

| 用途 | Windows | macOS |
|---|---|---|
| 设置 | `%APPDATA%\Chameleon\settings.json` | `~/Library/Application Support/Chameleon/settings.json` |
| 项目与资源 | `%LOCALAPPDATA%\Chameleon\projects\{projectId}\` | `~/Library/Application Support/Chameleon/projects/{projectId}/` |
| 临时资源缓存 | `%LOCALAPPDATA%\Chameleon\cache\` | `~/Library/Caches/Chameleon/` |
| 日志 | `%LOCALAPPDATA%\Chameleon\logs\` | `~/Library/Logs/Chameleon/` |

## 5. 核心接口

以下是契约草案。注释用英文，与代码规范一致。

### 5.1 图像

```csharp
namespace Chameleon.Core.Imaging
{
    /// <summary>An encoded image held in memory. Immutable; never decoded on the .NET side.</summary>
    public sealed class ImageData
    {
        public byte[] Bytes { get; }
        public string MimeType { get; }   // "image/png" | "image/jpeg" | "image/webp"
        public int Width { get; }
        public int Height { get; }

        /// <summary>Probes the header to fill MimeType/Width/Height. Throws FormatException if unsupported.</summary>
        public static ImageData FromBytes(byte[] bytes);
    }

    /// <summary>Reads format and pixel size from PNG / JPEG / WebP headers without decoding.</summary>
    public static class ImageProbe
    {
        public static bool TryProbe(byte[] bytes, out string mimeType, out int width, out int height);
    }
}
```

### 5.2 生成

```csharp
namespace Chameleon.Core.Generation
{
    public enum ReferenceRole { Style, Material, Object, Consistency }

    public sealed class ReferenceImage
    {
        public ImageData Image { get; set; }
        public ReferenceRole Role { get; set; }
        public string? Note { get; set; }          // optional user note, e.g. "use this wood for the floor"
    }

    public sealed class GenerationRequest
    {
        public string ModelId { get; set; }
        public string Prompt { get; set; }
        public string? SystemPrompt { get; set; }
        public ImageData? Source { get; set; }      // viewport capture or the image being edited
        public ImageData? Mask { get; set; }        // white = editable region; only sent if the model supports it
        public IList<ReferenceImage> References { get; }
        public string? AspectRatio { get; set; }    // null/empty = keep source ratio
        public string ImageSize { get; set; }       // "0.5K" | "1K" | "2K" | "4K"
        public IDictionary<string, string> Extras { get; }   // adapter-specific switches
    }

    public sealed class GenerationResult
    {
        public IReadOnlyList<ImageData> Images { get; }
        public string? Text { get; }                // text part returned by the model, if any
        public UsageInfo? Usage { get; }            // credits charged (cloud) or null (direct)
        public string RequestId { get; }
    }

    public sealed class BackendCapabilities
    {
        public bool SupportsMask { get; set; }
        public int MaxInputImages { get; set; }     // source + references + consistency images
        public IReadOnlyList<string> ImageSizes { get; set; }
        public IReadOnlyList<string> AspectRatios { get; set; }
        public int MaxConcurrency { get; set; }
    }

    /// <summary>One way of turning a GenerationRequest into images. UI and queue only see this.</summary>
    public interface IRenderBackend
    {
        string Id { get; }                           // "direct:{profileId}" | "cloud"
        string DisplayName { get; }
        Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct);
        BackendCapabilities GetCapabilities(string modelId);
        Task<GenerationResult> GenerateAsync(GenerationRequest request,
            IProgress<GenerationProgress>? progress, CancellationToken ct);
        /// <summary>Cheap credential check. Throws RenderException(AuthFailed) when rejected.</summary>
        Task ValidateAsync(string modelId, CancellationToken ct);
    }
}
```

### 5.3 服务商适配

```csharp
namespace Chameleon.Core.Providers
{
    /// <summary>Everything needed to reach one endpoint. Replaces the four legacy provider types.</summary>
    public sealed class ProviderProfile
    {
        public string Id { get; set; }               // "Gemini", "BltAI" for built-ins; GUID for user-defined
        public string DisplayName { get; set; }
        public string BaseUrl { get; set; }
        public string ApiFormat { get; set; }        // "gemini" | "openai"
        public string AuthType { get; set; }         // "goog" (x-goog-api-key) | "bearer"
        public IList<ModelInfo> Models { get; set; }
        public string DefaultModel { get; set; }
        public string? ApiKeyUrl { get; set; }       // where the user obtains a key
        public bool IsBuiltIn { get; set; }
        public bool EnableSearchGrounding { get; set; } = true;   // legacy always sent google_search
    }

    /// <summary>Endpoint, credential and shared HttpClient for one call; also redacts the key from text.</summary>
    public sealed class AdapterContext
    {
        public ProviderProfile Profile { get; }
        public string ApiKey { get; }
        public HttpClient HttpClient { get; }
        public string? Redact(string? text);
    }

    /// <summary>Translates between the neutral request model and one wire format. Stateless.</summary>
    public interface IProviderAdapter
    {
        string ApiFormat { get; }
        // promptText is the final text from PromptComposer; adapters never assemble prompts.
        HttpRequestMessage BuildGenerateRequest(AdapterContext context, GenerationRequest request, string promptText);
        Task<GenerationResult> ParseGenerateResponseAsync(AdapterContext context, HttpResponseMessage response, CancellationToken ct);
        HttpRequestMessage BuildValidateRequest(AdapterContext context, string modelId);
        Task<RenderException> ParseErrorAsync(AdapterContext context, HttpResponseMessage response, CancellationToken ct);
    }
}
```

`DirectBackend` 组合 `ProviderProfile` + `IProviderAdapter` + `ISecretStore` + 共享的 `HttpClient`。新增一种协议只需要新增一个适配器类并注册。

模型能力不由适配器给出：它是目录数据，由 `ProviderRegistry` 解析后放在 `ProviderProfile.Models[].Capabilities` 中。

### 5.4 设置与机密

```csharp
namespace Chameleon.Core.Settings
{
    public interface ISettingsStore
    {
        AppSettings Load();                          // never throws; returns defaults on failure
        void Save(AppSettings settings);             // atomic: write temp file then replace
        event EventHandler? Changed;
    }

    /// <summary>OS-backed secret storage. Keys are logical names such as "provider:Gemini".</summary>
    public interface ISecretStore
    {
        string? Get(string name);
        void Set(string name, string value);
        void Delete(string name);
        bool Contains(string name);
    }
}
```

### 5.5 任务队列

```csharp
namespace Chameleon.Core.Jobs
{
    public enum JobState { Queued, Running, Succeeded, Failed, Cancelled }

    public sealed class GenerationJob
    {
        public string Id { get; }
        public string BackendId { get; }
        public GenerationRequest Request { get; }
        public JobState State { get; }
        public GenerationResult? Result { get; }
        public RenderException? Error { get; }
        public string? GroupId { get; }             // batch or multi-candidate group
        public IReadOnlyList<string> DependsOn { get; }   // jobs whose results feed this one as consistency refs
    }

    public interface IGenerationQueue
    {
        GenerationJob Enqueue(string backendId, GenerationRequest request, JobOptions? options = null);
        void Cancel(string jobId);
        void CancelGroup(string groupId);
        IReadOnlyList<GenerationJob> Snapshot();
        event EventHandler<JobChangedEventArgs>? JobChanged;
    }
}
```

### 5.6 宿主抽象

```csharp
namespace Chameleon.App.Hosting
{
    /// <summary>Everything the app layer needs from Rhino. Implementations marshal to the UI thread.</summary>
    public interface IRhinoHost
    {
        HostInfo Info { get; }                       // platform, Rhino version, plug-in version
        Task<ImageData> CaptureActiveViewAsync(CaptureOptions options, CancellationToken ct);
        Task<IReadOnlyList<string>> GetNamedViewsAsync(CancellationToken ct);
        Task<ImageData> CaptureNamedViewAsync(string viewName, CaptureOptions options, CancellationToken ct);
        Task<string?> ShowSaveFileDialogAsync(SaveFileRequest request);
        Task<string?> ShowSelectFolderDialogAsync(string title);
        Task<IReadOnlyList<string>> ShowOpenFilesDialogAsync(OpenFilesRequest request);
        void OpenUrl(string url);
        void Log(string message);

        // Added in P3 (not declared yet: netstandard2.0 has no default interface members,
        // so they are added together with their implementations)
        DocumentInfo? GetActiveDocument();
        string? GetDocumentString(string key);
        void SetDocumentString(string key, string value);
        Task<ImageData?> CaptureSelectionMaskAsync(CaptureOptions options, CancellationToken ct);
        CameraState GetCameraState();                // active viewport camera, with a stable signature
        Task RestoreCameraAsync(CameraState camera);
        event EventHandler? ActiveDocumentChanged;
        event EventHandler? SelectionChanged;
    }
}
```

## 6. 数据模型

### 6.1 设置文件（版本 2）

```json
{
  "schemaVersion": 2,
  "language": "zh",
  "activeBackendId": "direct:BltAI",
  "activeModelByBackend": { "direct:BltAI": "gemini-3.1-flash-image-preview" },
  "userProviders": [ /* ProviderProfile，用户新增的 */ ],
  "builtInOverrides": { "BltAI": { /* 覆盖内置项的部分字段 */ } },
  "systemPrompt": null,
  "batch": { "mode": "serial", "maxConsistencyRefs": 3 },
  "cloud": { "baseUrl": null }
}
```

- API Key 不在这个文件里，存在 `ISecretStore` 中，名字为 `provider:{profileId}`。云端刷新令牌的名字为 `cloud:refreshToken`。
- `systemPrompt` 为 `null` 表示使用目录里的默认值。
- 旧文件的字段对应关系见 `legacy-inventory.md` 第 4 节。

### 6.2 内置目录

`Chameleon.Core/Catalog/catalog.json` 作为嵌入资源，包含：内置服务商及其模型（`id`、`displayName`、能力）、风格模板、宽高比列表、图片尺寸列表、默认系统提示词、一键操作模板（P3）。内容从现有代码原样迁入，见 `legacy-inventory.md` 第 2 节。账号模式下由服务端的 `/v1/catalog` 返回同样结构的目录。

### 6.3 项目与编辑会话（P3）

```
projects/{projectId}/
  project.json          # 会话索引：节点、父子关系、每个节点的生成配方
  assets/{sha256}.{ext} # 按内容寻址的图片文件，去重
```

```csharp
public sealed class ImageNode
{
    public string Id { get; set; }
    public string? ParentId { get; set; }
    public NodeKind Kind { get; set; }        // Capture | Generation | Edit | Import
    public string AssetId { get; set; }       // sha256 of the image file
    public DateTimeOffset CreatedAt { get; set; }
    public Recipe? Recipe { get; set; }       // how this node was produced; null for Capture/Import
    public string? Label { get; set; }        // e.g. named view name
    public CameraState? Camera { get; set; }  // Capture nodes only; used to align Rhino object masks
}

public sealed class Recipe
{
    public string BackendId { get; set; }
    public string ModelId { get; set; }
    public string Prompt { get; set; }
    public string? SystemPrompt { get; set; }
    public string? AspectRatio { get; set; }
    public string ImageSize { get; set; }
    public string? MaskAssetId { get; set; }
    public IList<RecipeReference> References { get; set; }
    public string? EditKind { get; set; }     // "inpaint" | "quick:time-of-day" | ...
}
```

`.3dm` 文件里只存一个字符串（`doc.Strings`，节 `Chameleon`，键 `ProjectId`）。图片不进 `.3dm`，避免文件膨胀。

## 7. 错误模型

```csharp
public enum RenderErrorCode
{
    InvalidRequest,       // bad parameters; not retryable
    AuthFailed,           // key rejected / token expired
    InsufficientCredits,  // cloud only
    RateLimited,          // 429; retryable
    ContentBlocked,       // provider safety filter
    ModelUnavailable,     // 404 model, or provider-side outage for this model
    Timeout,
    Network,
    ProviderError,        // 5xx or unparseable response
    NoImageReturned,      // 200 but the response carried no image
    Unknown
}

public sealed class RenderException : Exception
{
    public RenderErrorCode Code { get; }
    public int? HttpStatus { get; }
    public string? ProviderMessage { get; }    // raw provider text, truncated to 2 KB, secrets redacted
    public bool IsRetryable { get; }
}
```

- 取消用 `OperationCanceledException`，不包装成 `RenderException`。
- 桥接协议把 `Code` 转成字符串传给 UI，UI 按错误码显示本地化文案，并在详情里显示 `ProviderMessage`。
- 自动重试只针对 `RateLimited`、`Timeout`、`Network`、`ProviderError`，最多 2 次，指数退避，尊重 `Retry-After`。

## 8. 技术决策记录

| # | 决策 | 状态 | 理由 | 被否决的方案 |
|---|---|---|---|---|
| ADR-01 | UI 用 Eto 窗口 + WebView 承载 Web 前端 | 默认采用，待 P0 确认 | 蒙版、对比、版本树用 Canvas 实现成本低；一套代码两个平台；UI 可在浏览器里用假宿主开发和自动化测试 | 纯 Eto 原生控件：自绘编辑器成本高、样式能力弱。保留 WPF 另写 Mac 界面：两套代码 |
| ADR-02 | UI 与宿主的首选传输是本地回环 HTTP（`127.0.0.1` + 随机端口 + 路径令牌） | 默认采用，待 P0 确认 | 大图以二进制传输，`<img>` 直接加载；与 Eto 版本无关；开发宿主和真实宿主走同一条路 | 仅用 Eto 消息通道：4K 图要 base64 进脚本字符串，慢且占内存。保留为备选传输 |
| ADR-03 | 备选传输是 Eto 消息通道（`window.eto.postMessage` + `ExecuteScriptAsync`） | 备选 | macOS 的 WKWebView 若拒绝加载回环 HTTP，则切到此方案 | — |
| ADR-04 | 只支持 Rhino 8.19+ 与 Rhino 9 | 默认采用 | Rhino 7 的 macOS 版是 Mono，不可能共用；8.19 起 Eto 才有消息通道 | 继续支持 Rhino 7 Windows：要保留第二套 UI |
| ADR-05 | 像素操作全部放在 UI | 采用 | Core 不引入图像库，避免原生依赖在 macOS 上的签名与架构问题 | SkiaSharp / ImageSharp |
| ADR-06 | 新旧代码并存：旧 WPF 插件移入 `legacy/` 冻结，新代码在 `src/` 从零搭建 | 采用 | 旧工程在 Linux 上无法编译，agent 改动无法自检；并存期间旧版仍可发布 | 在旧工程上原地重构 |
| ADR-07 | JSON 库继续用 Newtonsoft.Json 13.0.3 | 采用 | 现有插件已在 Rhino 中验证可用；`netstandard2.0` 下无需额外包 | System.Text.Json：net48 下需要额外依赖，有版本冲突风险 |
| ADR-08 | 插件登录用设备授权流程（RFC 8628 的形态） | 采用 | 插件不处理密码；不需要本地回调端口；网页端以后加微信登录不用改插件 | 授权码 + PKCE + 回环回调：要处理端口占用和浏览器回跳 |
| ADR-09 | 服务端用 ASP.NET Core 8 + PostgreSQL，独立私有仓库 | 默认采用 | 与插件同语言，直接复用 Core 的适配器；本仓库公开，服务端业务逻辑不宜公开 | 直接部署开源中转网关：按张计费、设备登录、目录下发都要二次开发 |
| ADR-10 | 第一版充值只做兑换码 | 采用 | 不涉及支付资质与对账，可立即开始分发 | 首版即接入支付 |
| ADR-11 | 插件 GUID 沿用 `da1c6445-8347-41bf-8774-8c264cadf31d`，命令名 `Chameleon`，保留 `AIRender` 作为别名 | 采用 | Rhino 把新版识别为同一插件的升级；老用户的习惯命令继续可用 | 新 GUID：会与旧版并存、重复加载 |

## 9. 主要风险

| 风险 | 发现方式 | 应对 |
|---|---|---|
| macOS 的 WKWebView 不允许加载 `http://127.0.0.1` | P0 检查项 A | 切换到 ADR-03 的消息通道传输；协议层不变 |
| macOS WebView 内文本输入、中文输入法、拖放异常 | P0 检查项 D | 输入法问题无法绕过时，提示词输入改用 Eto 原生文本框叠加；拖放改用文件选择对话框 |
| macOS 上 `ViewCapture` 得到的 `Bitmap` 无法编码为 PNG | P0 检查项 C | 改用 `Bitmap` 的像素锁定接口自行编码，或经 Eto 的 `Bitmap` 转存 |
| 钥匙串在 Rhino 进程内不可用或频繁弹授权框 | P0 检查项 E | 退回加密文件存储（密钥派生自机器与用户标识），并在设置页说明 |
| 对象蒙版与截图不对齐 | P0 检查项 G、P3-T07 人工验收 | 蒙版与底图在同一次相机状态下、用相同尺寸连续截取 |
| 链式批量时参考图数量超过模型上限 | P1-T11 单元测试 | 能力里声明 `MaxInputImages`，队列按上限截断，保留最近的几张 |
| 转售上游接口可能受服务商条款约束；收款涉及资质 | 需人工确认，非开发任务 | 上线分发前由项目负责人核对上游条款和所在地区的合规要求 |
