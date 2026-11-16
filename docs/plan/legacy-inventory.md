# 现有代码盘点

现有插件（约 5,300 行，WPF + RhinoCommon 8）在 P1-T01 中原样移入 `legacy/`。本文件记录它的功能、必须保留的行为、已知缺陷和配置格式，作为新实现的对照依据。

文中路径是移动之后的路径；P1-T01 之前这些文件在仓库根目录，去掉 `legacy/` 前缀即可，行号不变。

## 1. 功能清单（P2 必须全部对齐）

P2-T11 的人工验收逐条核对这张表。「新实现」一列指明负责的任务。

### 1.1 单图模式

| # | 功能 | 现有实现位置 | 新实现 |
|---|---|---|---|
| S1 | 截取当前活动视口，尺寸等于视口像素尺寸 | `legacy/Services/ScreenCapture.cs:20`、`:35` | P2-T09 `RhinoHost.CaptureActiveViewAsync`，P2-T06 |
| S2 | 显示源图与结果图，点击图片全屏预览，Esc 或点击关闭 | `legacy/Views/AIRenderWindow.xaml.cs:127` 起 | P2-T06 |
| S3 | 选择模型（当前服务商的模型列表，内置模型显示别名） | `legacy/Models/RenderSettings.cs:95` 起 | P2-T06 |
| S4 | 风格模板下拉，选中后**替换**提示词内容 | `legacy/Models/RenderSettings.cs:213` 起 | P2-T06 |
| S5 | 提示词输入（多行） | `legacy/Views/AIRenderWindow.xaml` | P2-T06 |
| S6 | 宽高比：Original、1:1、4:3、3:2、16:9、9:16、21:9 | `legacy/Models/RenderSettings.cs:180` | P1-T04 目录，P2-T06 |
| S7 | 图片尺寸：0.5K、1K、2K、4K，默认 1K | `legacy/Models/RenderSettings.cs:206` | P1-T04 目录，P2-T06 |
| S8 | 生成；生成中禁用相关按钮；状态栏显示进度文字 | `legacy/ViewModels/AIRenderViewModel.cs:194` | P2-T06 |
| S9 | 保存结果为 PNG 或 JPEG，默认文件名 `AIRender_yyyyMMdd_HHmmss` | `legacy/ViewModels/AIRenderViewModel.cs:258` | P2-T06，`files.saveImage` |
| S10 | 「用作源图」：把结果变成新的源图，清空结果 | `legacy/ViewModels/AIRenderViewModel.cs:290` | P2-T06（P3 改为历史树上的子节点） |
| S11 | 「清空」：清除源图、结果、提示词 | `legacy/ViewModels/AIRenderViewModel.cs:250` | P2-T06 |
| S12 | 生成前自动保存当前服务商、模型选择 | `legacy/ViewModels/AIRenderViewModel.cs:54` | P2-T04 |

### 1.2 批量模式

| # | 功能 | 现有实现位置 | 新实现 |
|---|---|---|---|
| B1 | 读取文档的全部命名视图并立即逐个截图预览 | `legacy/ViewModels/BatchRenderViewModel.cs:111` | P2-T07 |
| B2 | 截取命名视图时临时切换活动视口相机，截完恢复原相机（位置、目标、上方向、镜头、投影方式） | `legacy/Services/ScreenCapture.cs:113` | P2-T09 |
| B3 | 每项可勾选；全选、全不选 | `legacy/ViewModels/BatchRenderViewModel.cs:53` | P2-T07 |
| B4 | 每项有独立的追加提示词，拼在共用提示词之后（以空行分隔） | `legacy/ViewModels/BatchRenderViewModel.cs:268` | P2-T07，P1-T11 |
| B5 | 单项重新截图 | `legacy/ViewModels/BatchRenderViewModel.cs:137` | P2-T07 |
| B6 | 链式渲染：按顺序串行；第 N 张把前面所有成功结果作为一致性参考一并发送；失败项不进入参考链 | `legacy/ViewModels/BatchRenderViewModel.cs:234` | P1-T11，P2-T07 |
| B7 | 单项重新生成：以其他所有已完成项的结果作为一致性参考 | `legacy/ViewModels/BatchRenderViewModel.cs:164` | P1-T11，P2-T07 |
| B8 | 风格参考图：拖入或点击选择一张图（png、jpg、jpeg、bmp、webp），每个请求都携带；可清除 | `legacy/Views/AIRenderWindow.xaml.cs:73` 起 | P2-T07 |
| B9 | 取消：当前请求完成后停止后续项 | `legacy/ViewModels/BatchRenderViewModel.cs:319` | P1-T11（新实现要能中断进行中的请求） |
| B10 | 进度：已完成数 / 总数；每项的状态文字 | `legacy/ViewModels/BatchRenderViewModel.cs` | P2-T07 |
| B11 | 单项保存；全部保存到所选文件夹，文件名为视图名（去除非法字符） | `legacy/ViewModels/BatchRenderViewModel.cs:323`、`:341` | P2-T07，`files.saveMany` |
| B12 | 使用 OpenAI 格式的服务商时不走链式，每张独立生成 | `legacy/ViewModels/BatchRenderViewModel.cs:277` | P1-T11（按能力判断，不按格式名判断） |

### 1.3 设置

| # | 功能 | 现有实现位置 | 新实现 |
|---|---|---|---|
| C1 | 服务商下拉：内置两项 + 用户自定义项 | `legacy/Services/SettingsService.cs:113` | P2-T08 |
| C2 | 每个服务商独立保存 API Key；切换服务商时载入对应 Key | `legacy/Views/SettingsWindow.xaml.cs:102` | P2-T08，P1-T10 |
| C3 | 「获取 API Key」链接，用系统浏览器打开服务商页面 | `legacy/Views/SettingsWindow.xaml.cs:131` | P2-T08，`shell.openUrl` |
| C4 | 测试 Key：Gemini 格式发一个仅文本的最小请求；OpenAI 格式请求 `/v1/models`；可选择用哪个模型测试 | `legacy/Views/SettingsWindow.xaml.cs:172` | P1-T06、P1-T07 的 `BuildValidateRequest`，P2-T08 |
| C5 | 新增自定义服务商：名称、Base URL、接口格式（gemini / openai）、鉴权方式（bearer / goog）、模型列表（逗号分隔）；选 openai 时自动填默认地址与模型并锁定鉴权方式 | `legacy/Views/AddProviderDialog.xaml.cs` | P2-T08 |
| C6 | 编辑自定义服务商；编辑内置服务商时保存为覆盖配置 | `legacy/Views/SettingsWindow.xaml.cs:235` | P2-T08，P1-T09 |
| C7 | 删除自定义服务商（连同其 Key）；若它是当前选中项则回退到默认 | `legacy/Services/SettingsService.cs:197` | P2-T08，P1-T09 |
| C8 | 语言：中文 / English，切换后界面立即刷新，无需重启；首次启动按系统语言选择 | `legacy/Services/Localization.cs`、`LocalizationManager.cs`、`SettingsService.cs:272` | P2-T05 |
| C9 | 系统提示词可编辑（界面上目前是隐藏的输入框） | `legacy/Views/AIRenderWindow.xaml` 第 441 行附近 | P2-T08 放入「高级」区域 |

### 1.4 其他

| # | 功能 | 现有实现位置 | 新实现 |
|---|---|---|---|
| O1 | 命令 `AIRender` 打开窗口，窗口以 Rhino 主窗口为父窗口 | `legacy/AIRendererCommand.cs` | P2-T09（新命令 `Chameleon`，保留 `AIRender`） |
| O2 | 插件名 Chameleon，GUID `da1c6445-8347-41bf-8774-8c264cadf31d` | `legacy/Properties/AssemblyInfo.cs` | P2-T09 |
| O3 | 推送 `v*` 标签时由 GitHub Actions 构建并发布 Release | `.github/workflows/release.yml` | P1-T01 调整路径，P2-T10 替换 |

## 2. 要原样迁入目录的内容

P1-T04 把下列内容搬进 `catalog.json`，文字逐字保留。

- **内置服务商**（`legacy/Models/ApiProvider.cs:97` 起）：
  - `Gemini`：显示名「Google原生」，`https://generativelanguage.googleapis.com`，鉴权 `goog`，模型 `gemini-3.1-flash-image-preview`（Nano Banana 2，默认）、`gemini-3-pro-image-preview`（Nano Banana Pro）、`gemini-2.5-flash-image`（Nano Banana），Key 获取地址 `https://aistudio.google.com/app/apikey`。
  - `BltAI`：显示名「柏拉图AI」，`https://hk-api.gptbest.vip`，鉴权 `bearer`，模型 `gemini-3.1-flash-image-preview`（默认）、`gemini-3-pro-image-preview`，Key 获取地址见源码第 139 行。首次启动的默认服务商是它。
- **默认系统提示词**：`legacy/Models/RenderSettings.cs:14`。
- **风格模板**：`legacy/Models/RenderSettings.cs:164`–`174`，共 11 项（含空的 Custom）。
- **宽高比与尺寸**：见上表 S6、S7。

P1-T05 把下列提示词片段搬进 `PromptComposer`，逐字保留：

| 片段 | 位置 | 用途 |
|---|---|---|
| 系统提示词与用户提示词的拼接方式：`{system}\n\n{prompt}` | `legacy/Services/AIRenderService.cs:55`–`57` | 所有请求 |
| 风格参考后缀：`A style reference image is also provided — match its lighting, atmosphere, and visual style.` | `legacy/Services/AIRenderService.cs:636`–`638` | 带风格参考图时 |
| 一致性指令（五行英文） | `legacy/Services/AIRenderService.cs:468`–`473` | 链式批量、单项重新生成 |

## 3. 接口行为（P1-T06、P1-T07 的对照依据）

### 3.1 Gemini 格式

- 地址：`{BaseUrl}/v1beta/models/{model}:generateContent`，POST，JSON。
- 鉴权：`AuthType == "goog"` 用请求头 `x-goog-api-key`，否则 `Authorization: Bearer`。
- 请求体：`contents[0].parts` 依次为文本、源图、一致性参考图（若有）、风格参考图（若有），图片为 `inline_data { mime_type, data(base64) }`；`tools: [{ google_search: {} }]`；`generationConfig.responseModalities: ["TEXT","IMAGE"]`；`generationConfig.imageConfig` 含 `imageSize`，宽高比非空时再加 `aspectRatio`。完整构造见 `legacy/Services/AIRenderService.cs:618`–`681`。
- 响应：取 `candidates[0].content.parts` 中第一个带 `inlineData.data` 的部分（`legacy/Services/AIRenderService.cs:208`–`247`）。
- 附加请求头：`Cache-Control: no-cache, no-store`、`X-Request-ID: {guid}`。

### 3.2 OpenAI Images 格式

- 地址：`{BaseUrl}/v1/images/edits`，POST，`multipart/form-data`。
- 字段：`image[]`（PNG 文件）、`prompt`、`model`、`n=1`。鉴权固定 `Authorization: Bearer`。
- 响应：`data[0].b64_json`，没有则下载 `data[0].url`（`legacy/Services/AIRenderService.cs:584`–`614`）。
- 现状不发送参考图，也不发送尺寸与宽高比。

## 4. 旧设置文件

位置：`%APPDATA%\AIRenderer\settings.json`（只存在于 Windows）。结构见 `legacy/Services/SettingsService.cs:10`–`22`。P1-T09 的迁移规则：

| 旧字段 | 新去处 |
|---|---|
| `ApiKeys`（键为 `Gemini` / `BltAI`） | `ISecretStore`，名字 `provider:{键}` |
| `CustomApiKeys`（键为自定义服务商 Id） | `ISecretStore`，名字 `provider:{Id}` |
| `CustomProviders` | `userProviders`，字段一一对应，`IsBuiltIn = false` |
| `BuiltInOverrides` | `builtInOverrides`，键不变 |
| `SelectedProviderId`（非空时）或 `SelectedProvider` | `activeBackendId = "direct:{id}"` |
| `SelectedModel` | `activeModelByBackend[activeBackendId]` |
| `LanguageIndex`（0 中文，1 英文） | `language`（`"zh"` / `"en"`） |

迁移完成后把旧文件改名为 `settings.migrated.json`，不删除。新文件已存在时不迁移。

## 5. 已知缺陷（新实现中不得重现）

| # | 缺陷 | 位置 | 新实现的要求 |
|---|---|---|---|
| L1 | 图片按 JPEG 编码，却声明 `mime_type: image/png` | `legacy/Services/AIRenderService.cs:119`、`:149` | `mime_type` 取自 `ImageData.MimeType`；P1-T06 有测试 |
| L2 | 四段几乎相同的 Gemini 请求构造（原生、柏拉图、自定义、链式） | `legacy/Services/AIRenderService.cs:113`、`:251`、`:452`、`:618` | 只有一个 `GeminiAdapter` |
| L3 | 每个请求清空并重设共享 `HttpClient.DefaultRequestHeaders`，并发不安全 | `legacy/Services/AIRenderService.cs:167` 等 | 请求头只设在 `HttpRequestMessage` 上 |
| L4 | 同步阻塞下载：`GetByteArrayAsync(url).Result` | `legacy/Services/AIRenderService.cs:437` | 全异步 |
| L5 | 所有失败返回 `null`，原因只写到 Rhino 命令行；界面只显示「生成失败」 | 整个服务层 | 抛 `RenderException`，界面显示错误码对应文案与服务商原始信息 |
| L6 | 取消只在两次请求之间生效，进行中的请求无法中断 | `legacy/ViewModels/BatchRenderViewModel.cs:258` | `CancellationToken` 传到 `HttpClient.SendAsync` |
| L7 | API Key 明文存在 `settings.json` | `legacy/Services/SettingsService.cs` | 系统密钥库 |
| L8 | 链式批量的参考图数量没有上限，视图多时请求体无限增大 | `legacy/Services/AIRenderService.cs:487`–`488` | 按 `MaxInputImages` 截断 |
| L9 | 单图模式不支持风格参考图（参考图区域只在批量模式显示，单图请求也不带参考图） | `legacy/ViewModels/AIRenderViewModel.cs:221` | 两种模式都支持 |
| L10 | 选中风格模板时判断的是名字不等于 `"None"`，而空模板名字是 `"Custom"`，所以选回 Custom 会清空提示词 | `legacy/Models/RenderSettings.cs:221`–`224` | 选 Custom 不改动提示词 |
| L11 | 部分界面文字未本地化（保存对话框标题、部分状态文字、新增服务商对话框） | `legacy/ViewModels/BatchRenderViewModel.cs:328`、`:349`，`legacy/Views/AddProviderDialog.xaml.cs` | 所有可见文字走 i18n |
| L12 | 「全部保存」用保存文件对话框冒充选文件夹 | `legacy/ViewModels/BatchRenderViewModel.cs:347`–`353` | 用文件夹选择对话框 |
| L13 | 未被引用的死代码：`GeminiAPIService.cs`、`GenerationsAPIService.cs`、`BatchRenderWindow.xaml(.cs)`，以及 `AIRenderService` 中未被调用的 `GenerateBltAIAsGenerationsAsync` | `legacy/Services/`、`legacy/Views/` | 不迁移 |
| L14 | 视图模型里直接调用 Rhino、文件对话框和设置存储，无法单元测试 | `legacy/ViewModels/` | 分层，见 `architecture.md` 第 3 节 |

## 6. 本地化键

现有键约 70 个，定义在 `legacy/Services/Localization.cs`。P2-T05 把它们全部迁到前端的 `zh.json` / `en.json`，键名改为点分小写形式（例如 `"Capture Active View"` → `action.captureActiveView`），并补上 L11 中缺失的文字。
