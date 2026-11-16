# P1 核心库

目标：在 `src/Chameleon.Core` 中实现与 UI、Rhino 都无关的全部业务逻辑，并用单元测试覆盖。
本阶段所有任务都可以在 Linux 上完整验证。旧插件原样保留在 `legacy/`，本阶段不修改它的代码。

通用验收命令（每个任务都要通过）：

```bash
dotnet build Chameleon.sln -c Release      # 0 warnings treated as errors in src/
dotnet test  Chameleon.sln -c Release
```

通用要求见 `AGENTS.md`。接口签名以 `architecture.md` 第 5 节为准。

---

## P1-T01 仓库重组、解决方案骨架、CI

**依赖**：无

### 目标

把现有 WPF 插件移入 `legacy/`，建立新解决方案的空骨架和持续集成。

### 步骤

1. 用 `git mv` 把以下内容移入 `legacy/`：`AIRenderer.csproj`、`AIRenderer.sln`、`AIRendererCommand.cs`、`AIRendererPlugin.cs`、`EmbeddedResources/`、`Models/`、`Services/`、`ViewModels/`、`Views/`、`Properties/`。`README.md`、`.gitignore`、`.github/`、`docs/` 留在根目录。
2. 修改 `.github/workflows/release.yml`：所有 `dotnet build` 加上 `legacy/AIRenderer.csproj`，打包路径由 `bin/Release/...` 改为 `legacy/bin/Release/...`。发布行为保持不变。
3. 新建 `Directory.Build.props`（只作用于 `src/`、`tests/`、`tools/`，在 `legacy/` 下放一个空的 `Directory.Build.props` 阻断继承）：
   - `LangVersion` 10.0，`Nullable` enable，`TreatWarningsAsErrors` true（`src/` 下），`NoWarn` 含 `NETSDK1138;NU1701`。
   - `Version` 2.0.0，`Company` Robin Song Design，`Product` Chameleon。
4. 新建 `Chameleon.sln`，包含：
   - `src/Chameleon.Core/Chameleon.Core.csproj`：`netstandard2.0`，引用 `Newtonsoft.Json` 13.0.3。
   - `src/Chameleon.App/Chameleon.App.csproj`：`netstandard2.0`，引用 Core。
   - `tests/Chameleon.Core.Tests/`、`tests/Chameleon.App.Tests/`：`net8.0`，xUnit（`xunit` 2.9.x、`xunit.runner.visualstudio`、`Microsoft.NET.Test.Sdk`）。
   - 每个工程放一个占位类型和一个占位测试，保证能构建和运行。
5. 新建 `.github/workflows/ci.yml`，在 push 和 pull request 时运行两个作业：
   - `core`（`ubuntu-latest`）：安装 .NET 8 SDK，`dotnet build Chameleon.sln -c Release`，`dotnet test Chameleon.sln -c Release`。
   - `legacy`（`windows-latest`）：`dotnet build legacy/AIRenderer.csproj -c Release -f net48` 与 `-f net7.0-windows`，确认移动后仍能构建。
6. 更新 `.gitignore`：增加 `node_modules/`、`dist/`、`*.yak`、`TestResults/`。
7. 更新根目录 `README.md`：说明仓库正处于重构期，旧版在 `legacy/`，计划在 `docs/plan/`。
8. 更新 `AGENTS.md` 的「当前状态」一节。

### 验收标准

- 通用验收命令通过。
- `git log --follow legacy/Services/AIRenderService.cs` 能追溯到移动前的历史。
- CI 的两个作业都成功（`legacy` 作业只能在 GitHub 上验证，提交后查看结果并写入最终回复）。
- `legacy/` 下除了路径变化没有任何内容改动：`git diff -M --stat HEAD~1 -- legacy/` 显示全部为纯重命名。

---

## P1-T02 图像基础类型与格式探测

**依赖**：P1-T01

### 目标

实现 `Chameleon.Core.Imaging.ImageData` 与 `ImageProbe`，不解码像素，只解析文件头。

### 实现要点

- PNG：校验 8 字节签名，从 IHDR 读宽高（大端）。
- JPEG：校验 `FF D8`，遍历段直到 SOF0–SOF15（排除 `C4`、`C8`、`CC`），读高和宽；要正确跳过 APPn、填充字节 `FF`。
- WebP：`RIFF....WEBP`，支持 `VP8 `（有损）、`VP8L`（无损）、`VP8X`（扩展）三种块。
- `ImageData` 不可变；`FromBytes` 探测失败抛 `FormatException`；提供 `Sha256Hex` 惰性属性，供资源库寻址。
- 提供 `ImageData.FileExtension`（`png` / `jpg` / `webp`）。

### 测试

在 `tests/Chameleon.Core.Tests/Fixtures/` 放入小尺寸样例文件：PNG、基线 JPEG、渐进式 JPEG、带 EXIF 的 JPEG、三种 WebP，各自断言格式与宽高；另测截断文件、空数组、非图片数据返回失败。样例文件用脚本生成并提交二进制（每个不超过 20 KB）。

### 验收标准

通用命令通过；`Chameleon.Core` 的引用列表里没有 `System.Drawing`。

---

## P1-T03 生成请求、结果、能力、错误模型、`IRenderBackend`

**依赖**：P1-T02

### 目标

落地 `architecture.md` 5.2 节与第 7 节的类型，作为后续所有任务的公共语言。

### 实现要点

- 命名空间 `Chameleon.Core.Generation`。
- `GenerationRequest.Validate(BackendCapabilities)`：检查模型非空、提示词非空、尺寸在能力列表内、输入图片总数不超过 `MaxInputImages`、有蒙版但能力不支持时报错。失败抛 `RenderException(InvalidRequest)`，消息指明是哪一项。
- `RenderException` 的构造辅助：`FromHttpStatus(int status, string? body)`，映射规则：400 → `InvalidRequest`，401/403 → `AuthFailed`，402 → `InsufficientCredits`，404 → `ModelUnavailable`，408 → `Timeout`，429 → `RateLimited`，5xx → `ProviderError`，其余 → `Unknown`。
- `ProviderMessage` 截断到 2,048 字符；提供 `Redactor.Redact(string text, params string[] secrets)` 把出现的 Key 替换为 `***`。
- `ModelInfo { Id, DisplayName, Capabilities }`、`UsageInfo { CreditsCharged }`、`GenerationProgress { Stage, Message }`。

### 测试

校验规则逐条测试；状态码映射表逐项测试；脱敏测试。

### 验收标准

通用命令通过；公开类型都有英文 XML 文档注释。

---

## P1-T04 服务商配置与内置目录

**依赖**：P1-T03

### 目标

用单一的 `ProviderProfile` 取代旧的四个类型，并把内置数据搬进嵌入的 JSON 目录。

### 涉及文件

- `src/Chameleon.Core/Providers/ProviderProfile.cs`
- `src/Chameleon.Core/Catalog/catalog.json`（嵌入资源）
- `src/Chameleon.Core/Catalog/Catalog.cs`、`CatalogLoader.cs`
- `src/Chameleon.Core/Providers/ProviderRegistry.cs`

### 实现要点

- `catalog.json` 的内容按 `legacy-inventory.md` 第 2 节逐字迁入：两个内置服务商、模型别名、风格模板、宽高比、尺寸、默认系统提示词。风格模板增加稳定的 `id`（如 `arch-render`、`mir`、`sketch`）和中英文名称；提示词文本不改。
- 模型能力的初始值：Gemini 格式的模型 `supportsMask=false`、`maxInputImages=4`、`imageSizes` 为四档、`maxConcurrency=2`；这些数值写在目录里，便于以后按服务商文档调整。执行本任务时查阅服务商当前文档，核对各模型实际支持的尺寸与输入图片数量上限，把依据写在提交说明里。
- `ProviderRegistry`：输入内置目录 + `AppSettings.UserProviders` + `AppSettings.BuiltInOverrides`，输出合并后的 `IReadOnlyList<ProviderProfile>`。覆盖规则与旧逻辑一致（`legacy/Services/SettingsService.cs:113`–`151`）：覆盖项中非空字段替换内置值，`ApiKeyUrl` 始终取内置值。
- 目录提供按语言取显示名的方法。

### 测试

目录能加载且通过结构校验（模型 id 唯一、默认模型存在于模型列表）；合并规则的各种组合；风格模板提示词与旧代码中的字符串逐字相等（测试里直接写入期望值）。

### 验收标准

通用命令通过。

---

## P1-T05 提示词组合器

**依赖**：P1-T03

### 目标

`Chameleon.Core.Prompts.PromptComposer` 是唯一负责拼装最终提示词文本的地方。

### 实现要点

```csharp
public sealed class PromptComposer
{
    /// <summary>Builds the final text sent to the model from the request's parts.</summary>
    public string Compose(GenerationRequest request);
}
```

规则，按顺序拼接，段落之间用 `\n\n`：

1. 系统提示词（非空时）。
2. 用户提示词。
3. 存在 `Style` 角色的参考图时，追加风格参考后缀（原文见 `legacy/Services/AIRenderService.cs:637`）。
4. 存在 `Consistency` 角色的参考图时，追加一致性指令（原文见 `legacy/Services/AIRenderService.cs:468`–`473`）。
5. `Material`、`Object` 角色的参考图各有一句说明（P3-T09 时补充，现在留出扩展点：角色到文案的映射表）。
6. 参考图带 `Note` 时，按图片顺序逐条列出。

文案常量集中在 `PromptTexts` 静态类中。组合结果必须与旧代码在相同输入下逐字相同（仅系统提示词 + 用户提示词；带风格参考；带一致性参考；两者都带）。

### 测试

四种组合的逐字比对；空系统提示词；空白提示词的处理。

---

## P1-T06 Gemini 适配器

**依赖**：P1-T04、P1-T05

### 目标

`GeminiAdapter : IProviderAdapter`，一个类覆盖旧代码里的四段请求构造。

### 实现要点

- 请求格式按 `legacy-inventory.md` 3.1 节。图片顺序：源图 → `Consistency` 参考 → 其余参考。
- `mime_type` 取自各 `ImageData.MimeType`（修复缺陷 L1）。
- `tools: [{ google_search: {} }]` 仅在 `ProviderProfile.EnableSearchGrounding` 为真时发送。
- 鉴权头按 `AuthType`。附加头 `X-Request-ID`。
- 请求体用流式方式序列化，避免把多张大图的 base64 在内存里重复拷贝。
- 解析：遍历 `candidates[0].content.parts`，收集所有 `inlineData`（兼容 `inline_data` 写法）为图片，拼接所有 `text` 为文本。
- 错误映射：
  - 非 2xx：解析 `error.status` / `error.message`；`API_KEY_INVALID`、`PERMISSION_DENIED`、`UNAUTHENTICATED` → `AuthFailed`；`RESOURCE_EXHAUSTED` → `RateLimited`；`NOT_FOUND` → `ModelUnavailable`；其余按 `RenderException.FromHttpStatus`。
  - 2xx 但 `promptFeedback.blockReason` 存在，或 `finishReason` 为 `SAFETY`、`PROHIBITED_CONTENT`、`IMAGE_SAFETY` → `ContentBlocked`。
  - 2xx 且没有任何图片 → `NoImageReturned`，`ProviderMessage` 带上模型返回的文本。
- `BuildValidateRequest`：仅文本的最小请求（`contents` 为 `"Hi"`，`responseModalities: ["TEXT"]`），同 `legacy/Views/SettingsWindow.xaml.cs:204`–`208`。
- 能力取自目录中该模型的声明；目录里没有的模型（用户自填的模型名）用该格式的默认能力。

### 测试

- 请求快照测试：给定请求，断言 URL、请求头、JSON 结构（与 `tests/.../Fixtures/gemini/` 下的期望 JSON 比对，图片数据用占位短串）。
- 响应测试：成功（单图、多图、图文混合）、各类错误体、安全拦截、空响应、非 JSON 响应。响应样例放在 Fixtures 目录。
- L1 回归测试：源图为 JPEG 时 `mime_type` 为 `image/jpeg`。

---

## P1-T07 OpenAI Images 适配器

**依赖**：P1-T04、P1-T05

### 目标

`OpenAIImagesAdapter : IProviderAdapter`，对应 `/v1/images/edits`。

### 实现要点

- 请求格式按 `legacy-inventory.md` 3.2 节，在此基础上：
  - 参考图作为额外的 `image[]` 部分追加（数量受能力限制）。
  - 有蒙版时作为 `mask` 部分发送；`supportsMask` 在该格式的默认能力中为 `true`。
  - `size` 字段：按宽高比与尺寸档位映射到接口支持的取值；执行本任务时查阅 OpenAI 当前的图像接口文档，确认 `gpt-image` 系列支持的 `size` 取值与输入图片数量上限，把映射表写在代码注释和提交说明里。无法映射时不发送 `size`。
- 解析：`data[]` 中每项优先 `b64_json`，否则异步下载 `url`（传入 `CancellationToken`）。
- 错误映射：`error.code` 为 `invalid_api_key` → `AuthFailed`；`rate_limit_exceeded` → `RateLimited`；`content_policy_violation` 或 `moderation_blocked` → `ContentBlocked`；`insufficient_quota` → `RateLimited`（消息中注明额度不足）；其余按状态码。
- `BuildValidateRequest`：`GET {BaseUrl}/v1/models`。

### 测试

multipart 结构（各部分名称、文件名、内容类型）；带蒙版与多参考图；两种响应形式；错误映射。

---

## P1-T08 `DirectBackend` 与 HTTP 管线

**依赖**：P1-T06、P1-T07

### 目标

`DirectBackend : IRenderBackend`，把配置、适配器、Key、HTTP 调用和重试组合起来。

### 实现要点

- 构造参数：`ProviderProfile`、`IProviderAdapter`、`ISecretStore`、`HttpClient`、`PromptComposer`、可选的 `RetryPolicy`。
- `Id` 为 `direct:{profile.Id}`。
- `GenerateAsync` 流程：取 Key（没有则抛 `AuthFailed`，消息说明未设置）→ `request.Validate` → 组合提示词 → 适配器构造请求 → `HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)` → 适配器解析。
- 超时：单次请求 5 分钟，用链接的 `CancellationTokenSource` 实现，超时转为 `RenderException(Timeout)`；调用方取消则抛 `OperationCanceledException`。
- 重试：只对 `IsRetryable` 的错误，最多 2 次，退避 2 秒、6 秒，带 ±20% 抖动，遵守 `Retry-After`（上限 30 秒）。每次重试重新构造 `HttpRequestMessage`。
- 网络异常（`HttpRequestException`、`IOException`）转为 `RenderException(Network)`。
- 所有进入异常和日志的文本先经 `Redactor` 去掉 Key。
- `BackendFactory`：按 `ProviderProfile.ApiFormat` 从适配器注册表选适配器；未知格式抛 `InvalidRequest`。
- 共享一个 `HttpClient`（`Timeout` 设为无限，由上面的机制控制），不修改 `DefaultRequestHeaders`（修复缺陷 L3）。

### 测试

用自定义 `HttpMessageHandler` 模拟：成功；401；先 429 后成功（断言重试次数与等待，时间用可注入的延迟函数）；持续 500 直到放弃；超时；调用方取消；并发 8 个请求互不串头（断言每个请求的鉴权头与 `X-Request-ID` 各自独立）。

---

## P1-T09 设置存储与旧配置迁移

**依赖**：P1-T04

### 目标

实现版本 2 的设置文件读写，以及从旧 `settings.json` 的一次性迁移。

### 涉及文件

- `src/Chameleon.Core/Settings/AppSettings.cs`、`ISettingsStore.cs`、`JsonSettingsStore.cs`
- `src/Chameleon.Core/Settings/ISecretStore.cs`、`InMemorySecretStore.cs`
- `src/Chameleon.Core/Settings/LegacySettingsMigrator.cs`

### 实现要点

- 结构见 `architecture.md` 6.1 节。
- `JsonSettingsStore(string filePath)`：读取失败（不存在、损坏）返回默认值并把损坏文件另存为 `settings.corrupt-{时间戳}.json`；保存用「写临时文件 → 替换」保证原子性；进程内加锁。
- 默认语言：系统界面语言以 `zh` 开头为中文，否则英文。
- 默认后端：`direct:BltAI`，默认模型为其默认模型（与旧行为一致）。
- `LegacySettingsMigrator.TryMigrate(legacyPath, ISettingsStore, ISecretStore)`：规则见 `legacy-inventory.md` 第 4 节。旧文件中 `ApiKeys` 的键可能是枚举名字符串，也可能是数字（Newtonsoft 对枚举键的两种序列化形式），两种都要处理。返回迁移了多少个 Key。
- `ProviderKeyNames.For(profileId)` 返回 `provider:{profileId}`。

### 测试

读写往返；损坏文件；并发保存；迁移：准备若干旧文件样例（仅内置 Key、含自定义服务商、含内置覆盖、选中自定义服务商、数字枚举键），断言新设置与密钥库内容；重复迁移不产生副作用。

---

## P1-T10 密钥存储的平台实现

**依赖**：P1-T09

### 目标

在 `src/Chameleon.App/Platform/` 实现系统级的 `ISecretStore`。

### 实现要点

- `WindowsDpapiSecretStore`：每个机密用 `ProtectedData.Protect(…, DataProtectionScope.CurrentUser)` 加密后存为 `%APPDATA%\Chameleon\secrets\{名字的 SHA-256}.bin`。`netstandard2.0` 下引用 NuGet 包 `System.Security.Cryptography.ProtectedData`。
- `MacKeychainSecretStore`：P/Invoke `Security.framework`，服务名 `com.robinsongdesign.chameleon`，账户名为机密名字。
- `EncryptedFileSecretStore`：兜底实现，AES-GCM 不可用于 `netstandard2.0`，用 AES-CBC + HMAC-SHA256，密钥由「机器标识 + 用户名 + 固定盐」经 PBKDF2 派生。文档注释里写明它只能防止明文泄露，不能防止同机恶意程序。
- `SecretStoreFactory.Create(IAppPaths)`：Windows → DPAPI；macOS → 钥匙串，初始化自检失败则退回加密文件；其他系统（Linux 开发宿主）→ 加密文件。P0 报告若表明钥匙串不可用（见 `phase-0-spikes.md` 的决策表），macOS 直接用加密文件。
- `AppPaths : IAppPaths`：按 `architecture.md` 4.4 节给出各目录，macOS 路径由 `HOME` 环境变量拼出。

### 测试

`EncryptedFileSecretStore` 全量测试（往返、覆盖、删除、文件被篡改时读取返回 `null`）。平台实现的测试用运行时判断跳过不匹配的系统；Linux CI 上只跑兜底实现。`AppPaths` 的路径拼接用注入的环境提供者测试三种系统。

### 验收标准

通用命令通过；`Chameleon.App` 不引用 RhinoCommon。

---

## P1-T11 任务队列与批量计划

**依赖**：P1-T03

### 目标

`GenerationQueue : IGenerationQueue` 负责并发、取消、依赖和状态通知；`BatchPlanner` 把一组视图变成带依赖关系的任务。

### 实现要点

- 队列持有「后端 id → `IRenderBackend`」的解析委托，不直接依赖具体后端。
- 并发上限按后端分别计算，取 `BackendCapabilities.MaxConcurrency`。
- 任务带 `DependsOn`：所有依赖进入终态后才可运行。运行前把依赖中**成功**任务的首张结果图作为 `Consistency` 参考追加到请求（失败、取消的依赖跳过，对应旧行为 B6）。
- 追加后若输入图片总数超过 `MaxInputImages`，丢弃最早的一致性参考，保留最近的（修复缺陷 L8）；用户显式提供的参考图不丢弃。
- 取消：排队中的直接置为 `Cancelled`；运行中的取消其 `CancellationToken`（修复缺陷 L6）。`CancelGroup` 取消整组。
- 每次状态变化触发 `JobChanged`，事件在线程池线程上触发，订阅者自行切线程。
- `BatchPlanner.Plan(items, mode, capabilities)`：
  - `Serial`：第 i 个任务依赖前 i-1 个。
  - `AnchorParallel`：第 1 个无依赖，其余只依赖第 1 个。
  - `Independent`：无依赖。
  - `capabilities.MaxInputImages < 2` 时强制 `Independent`（对应旧行为 B12）。
- 每项的提示词为「共用提示词 + `\n\n` + 追加提示词」（追加为空时不加分隔）。
- 已完成的任务保留在内存中直到进程退出或显式清理，上限 200 条，超出时丢弃最早的终态任务。

### 测试

用可控的假后端（按信号完成、可注入失败）：并发上限；依赖顺序；失败项不进入后续参考；参考图截断；取消排队中与运行中的任务；整组取消；三种批量模式生成的依赖图；事件顺序。

---

## P1-T12 命令行冒烟工具

**依赖**：P1-T08

### 目标

`tools/Chameleon.Cli`（`net8.0` 控制台）：不经过 Rhino 和 UI，用真实 Key 调真实接口，供人和 agent 验证适配器。

### 用法

```bash
export CHAMELEON_API_KEY=...        # never passed on the command line
dotnet run --project tools/Chameleon.Cli -- generate \
  --provider BltAI --model gemini-3.1-flash-image-preview \
  --source sample.png --prompt "photorealistic render" \
  --size 1K --aspect 16:9 --ref style.jpg:style --out out.png

dotnet run --project tools/Chameleon.Cli -- validate --provider Gemini
dotnet run --project tools/Chameleon.Cli -- providers        # list built-in providers and models
```

- 也支持 `--base-url`、`--format`、`--auth` 临时指定一个自定义服务商。
- 失败时打印错误码、HTTP 状态、已脱敏的服务商消息，退出码非 0。
- Key 只从环境变量读取。

### 验收标准

- `providers` 子命令在无网络、无 Key 时可运行并列出内置目录。
- 没有 Key 时 `generate` 以清晰的提示退出。
- 用真实 Key 的端到端验证由持有 Key 的人执行；agent 没有 Key 时在最终回复里注明「未做真实接口验证」。

---

## 本阶段完成的标志

- `Chameleon.Core` 覆盖旧 `AIRenderService`、`SettingsService`、`ApiProvider`、`RenderSettings` 中的全部业务逻辑。
- `legacy-inventory.md` 第 5 节的缺陷 L1–L8 在新代码中都有对应的测试。
- 用命令行工具配合真实 Key 至少成功生成过一张图（Gemini 格式与 OpenAI 格式各一次）。
