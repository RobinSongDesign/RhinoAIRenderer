# P2 跨平台宿主与 UI

目标：用 Eto 窗口 + WebView + Web 前端替换 WPF 界面，功能与现有版本完全对齐（清单见 `legacy-inventory.md` 第 1 节），在 Windows 和 macOS 上发布。
本阶段结束时删除 `legacy/`。

本文件按 ADR-02（回环 HTTP 传输）编写。P0-T3 若改变了传输方式，以更新后的本文件为准。

## 验证方式

| 层 | 工具 | 命令 |
|---|---|---|
| .NET | xUnit | `dotnet test Chameleon.sln -c Release` |
| 前端静态检查 | TypeScript、ESLint | `pnpm --dir src/Chameleon.UI typecheck`、`pnpm --dir src/Chameleon.UI lint` |
| 前端单元测试 | Vitest | `pnpm --dir src/Chameleon.UI test` |
| 前端构建 | Vite | `pnpm --dir src/Chameleon.UI build` |
| 端到端 | Playwright + 开发宿主 | `pnpm --dir src/Chameleon.UI e2e` |
| Rhino 内行为 | 人工 | P2-T11 清单 |

端到端测试跑的是「真实前端 + 真实 `Chameleon.App` + 真实 `Chameleon.Core` + 假 Rhino 宿主 + 假后端」，只有 Rhino 本身和真实服务商被替换。

---

## P2-T01 桥接 DTO、RPC 路由、事件总线

**依赖**：P1-T03

### 目标

在 `src/Chameleon.App/Bridge/` 实现与传输无关的协议层。

### 实现要点

- `Dto/`：`bridge-protocol.md` 第 3、4、6 节中 P2 阶段的全部类型。JSON 序列化统一用一个 `BridgeJson` 配置（camelCase、忽略 null、枚举转小写字符串）。
- `IRpcHandler`：`string Method { get; }` 与 `Task<object?> HandleAsync(JToken? params, CancellationToken ct)`。提供泛型基类 `RpcHandler<TParams, TResult>` 负责参数反序列化与校验。
- `RpcRouter`：注册处理器；`Task<RpcResponse> DispatchAsync(RpcRequest, ct)`；未知方法 → `invalid_request`；异常映射：`RenderException` → 第 5 节对应错误码，`OperationCanceledException` → `cancelled`，`RhinoHostException` → `rhino_error`，`KeyNotFoundException` → `not_found`，其余 → `internal`（`detail` 只放异常类型与消息，不放堆栈）。
- `EventBus`：`Publish(string eventName, object data)`；多订阅者；每个订阅者有界队列（容量 256，溢出时丢弃最早的并记日志），慢订阅者不阻塞发布者。
- `BridgeErrorMapper`：`RenderErrorCode` ↔ 协议错误码的唯一映射表。

### 测试

路由分发；参数缺失与类型错误；每种异常的映射；事件的多订阅者与溢出行为。

---

## P2-T02 本地回环服务器

**依赖**：P2-T01

### 目标

`src/Chameleon.App/Server/LocalServer`：按 `bridge-protocol.md` 1.1 节提供静态文件、RPC、事件流、资源上传下载。

### 实现要点

- 基于 `System.Net.HttpListener`。端口选取：用 `TcpListener(IPAddress.Loopback, 0)` 取得空闲端口后释放，再启动 `HttpListener`；失败重试最多 5 次。
- 令牌：`RandomNumberGenerator` 生成 32 字节，十六进制。`BaseUrl` 属性返回 `http://127.0.0.1:{port}/{token}/`。
- 请求处理在线程池上并发进行，接受循环不被单个请求阻塞。
- 安全检查的顺序：`Host` 头 → 令牌前缀 → `Origin` 头（若存在）。任何一项不通过都返回 404 或 403，且不泄露原因。
- 静态文件通过 `IStaticFileProvider` 读取，两个实现：`DirectoryFileProvider`（开发宿主）与 `EmbeddedResourceFileProvider`（插件）。未知路径回退到 `index.html`；禁止路径穿越；按扩展名设置 `Content-Type`；`index.html` 不缓存，带哈希的资源文件长缓存。
- `/rpc`：读取 JSON → `RpcRouter.DispatchAsync` → 写回 JSON。请求体上限 1 MB。
- `/events`：订阅 `EventBus`，按 SSE 格式写出；每 15 秒写一行注释保活；客户端断开时取消订阅。
- `/assets/{id}` GET：从 `IAssetStore` 取流式输出，支持 `ETag`（即 assetId）与 304。
- `/assets` POST：限制 64 MB；读入后 `ImageData.FromBytes` 校验是图片，存入 `IAssetStore`，返回 `AssetRef`。
- 开发模式选项 `AllowedDevOrigin`：只在开发宿主中设置，允许 Vite 开发服务器的源，并对该源返回 CORS 头。插件中永远为 `null`。
- `Dispose` 时停止监听、结束所有事件流。

### 测试

用 `HttpClient` 对真实启动的服务器做集成测试：静态文件与回退；RPC 往返；事件流收到发布的事件；资源上传后下载字节一致；错误令牌 404；错误 `Host` 头被拒；外来 `Origin` 被拒；路径穿越被拒；超限上传被拒；并发 20 个 RPC。

---

## P2-T03 资源库、`IRhinoHost`、开发宿主

**依赖**：P2-T02、P1-T11

### 目标

补齐让整个应用脱离 Rhino 运行所需的部件。

### 实现要点

- `IAssetStore` / `DiskAssetStore`（`src/Chameleon.App/Assets/`）：按内容寻址存到缓存目录 `{sha256}.{ext}`；`Put(ImageData) → AssetRef`、`TryGet(assetId) → ImageData?`、`OpenRead(assetId)`；相同内容只存一份；启动时清理超过 7 天未访问的缓存文件（P3 的项目资源存在别处，不受影响）。
- `IRhinoHost`、`HostInfo`、`CaptureOptions`、`SaveFileRequest`、`OpenFilesRequest`、`RhinoHostException`（`src/Chameleon.App/Hosting/`），签名见 `architecture.md` 5.6 节。P3 才用到的成员到 P3 再加入接口。
- `ChameleonApp.Start(ChameleonAppOptions)`（`src/Chameleon.App/`）：唯一的组合根。输入 `IRhinoHost`、`IAppPaths`、`IStaticFileProvider`、可选的额外后端；创建设置存储、密钥存储、目录、服务商注册表、后端工厂、队列、资源库、路由、事件总线、服务器；返回可释放的 `ChameleonApp`，暴露 `BaseUrl`。
- `tools/Chameleon.DevHost`（`net8.0` 控制台）：
  - `FakeRhinoHost`：截图返回 `samples/` 目录下的样例图（活动视口一张，命名视图 `Exterior`、`Interior`、`Aerial` 各一张）；文件对话框把结果写到 `--out` 目录并返回路径；`OpenUrl` 只打印。
  - `FakeBackend : IRenderBackend`（id 为 `fake`）：延迟 `--delay` 毫秒后返回 `samples/results/` 下的样例结果图（按请求序号轮换）。提示词中出现 `#fail:{错误码}` 时抛对应的 `RenderException`，出现 `#slow` 时延迟 10 秒，用于测试错误展示与取消。
  - 参数：`--port`、`--token`、`--ui {dist 目录}`、`--data {数据目录，默认临时目录}`、`--allow-origin`、`--delay`。启动后打印完整地址。
  - `--fake-providers`：在本地应答服务商的 HTTP 请求（唯一被接受的 Key 是 `dev-good-key`），让自带 Key 的流程（设置 Key、测试 Key、经服务商生成）可以离线跑通，供端到端测试使用。
  - 同时注册真实的 `DirectBackend`，这样在本机设置 Key 后可以用开发宿主做真实接口调试。
- 样例图片提交到 `tools/Chameleon.DevHost/samples/`，尺寸不超过 1280×720，每张不超过 200 KB。

### 测试

`DiskAssetStore` 的去重、清理；`AppBootstrapper` 用假宿主能启动并响应一次 RPC。

### 验收标准

`dotnet run --project tools/Chameleon.DevHost -- --port 5199 --token dev` 能启动；`curl http://127.0.0.1:5199/dev/rpc -d '{"id":"1","method":"app.getState"}'` 返回 JSON（此时处理器可能尚未齐全，返回 `invalid_request` 也算通过，处理器在 P2-T04 实现）。

---

## P2-T04 应用服务与 RPC 处理器

**依赖**：P2-T03、P1-T08、P1-T09

### 目标

实现 `bridge-protocol.md` 第 4 节中标注为 P2 的全部方法和第 6 节中 P2 的全部事件。

### 实现要点

处理器只做参数转换，逻辑放在 `src/Chameleon.App/Services/` 的应用服务里：

| 服务 | 职责 | 对应方法 |
|---|---|---|
| `AppStateService` | 汇总状态；语言与设置更新 | `app.getState`、`settings.update`、`log.write`、`shell.openUrl` |
| `BackendService` | 由服务商注册表与密钥存储生成 `BackendInfo` 列表；切换当前后端与模型并持久化；变化时发 `backends.changed` | `backend.select` |
| `ProviderService` | 服务商增删改、内置覆盖与重置、Key 写入与测试 | `providers.*` |
| `CaptureService` | 调 `IRhinoHost` 截图并存入资源库 | `rhino.captureActiveView`、`rhino.listNamedViews`、`rhino.captureNamedView` |
| `JobService` | 把 `GenerateParams` 转为 `GenerationRequest`（从资源库取图）；提交到队列；把队列事件转为 `job.updated`；结果图存入资源库 | `jobs.*` |
| `FileService` | 保存单张、批量保存、选择图片导入 | `files.*` |

- `providers.testKey` 调后端的 `ValidateAsync`，带 `key` 参数时用临时的内存密钥存储构造后端，不落盘。
- `providers.delete` 同时删除对应 Key；删除的是当前后端时回退到默认后端（旧行为 C7）。
- `jobs.submit` 前检查后端 `ready`，否则 `not_ready`。
- `jobs.submitBatch` 用 `BatchPlanner`。
- `files.saveMany` 的文件名去除非法字符（旧行为 B11），同名时追加序号。
- `shell.openUrl` 只接受 `https:`。
- 首次启动时执行旧设置迁移（`LegacySettingsMigrator`），仅 Windows。

### 测试

在 `tests/Chameleon.App.Tests/` 用假宿主、假后端、内存密钥存储，通过真实的 `LocalServer` 调用每个方法：正常路径与主要错误路径；事件是否发出；Key 不出现在任何响应里（对全部响应做字符串断言）。

---

## P2-T05 UI 工程骨架

**依赖**：P2-T03

### 目标

建立 `src/Chameleon.UI`：工程配置、桥接客户端、状态管理、国际化、基础组件、测试设施。

### 技术栈

React 18、TypeScript（strict）、Vite、Zustand（状态）、Vitest + Testing Library（单元测试）、Playwright（端到端）、ESLint + Prettier。包管理器 pnpm。不引入 UI 组件库，样式用 CSS 变量 + CSS Modules。

### 目录

```
src/Chameleon.UI/
  index.html
  vite.config.ts            # build.target: ['chrome100','safari15']; base: './'
  playwright.config.ts      # webServer 启动 Chameleon.DevHost
  src/
    main.tsx, App.tsx
    bridge/                 # types.ts, transport.ts, httpTransport.ts, bridge.ts
    state/                  # appStore.ts, jobsStore.ts
    i18n/                   # zh.json, en.json, index.ts（t() 与 useT()）
    components/             # Button, Select, TextArea, ImageView, PreviewOverlay, DropZone, StatusBar, ErrorNotice, Dialog
    imaging/                # loadImage, canvasToBlob, convertFormat（PNG↔JPEG）
    features/               # single/, batch/, settings/（后续任务填充）
    styles/                 # tokens.css（颜色、间距、字号变量；浅色与深色）
  e2e/                      # Playwright 用例
```

### 实现要点

- `HttpTransport`：桥接基址取页面自身地址（`new URL('.', location.href)`）；开发时由 `VITE_BRIDGE_URL` 覆盖。`call` 用 `fetch`；`on` 用单个 `EventSource` 多路分发并自动重连；`assetUrl` 拼接；`upload` 发送 `Blob`。
- `bridge.call` 收到带 `error` 的响应时抛 `BridgeError`（含 `code`、`detail`、`retryable`）。
- `appStore`：启动时 `app.getState`，订阅 `backends.changed`、`settings.changed`；`jobsStore`：订阅 `job.updated`，提供按 id 与按组查询。
- i18n：把 `legacy/Services/Localization.cs` 的全部键迁入，键名改为点分形式；补齐 `legacy-inventory.md` 缺陷 L11 提到的文字；增加每个错误码的文案（`error.auth_failed` 等）。语言来自 `appStore`，切换后界面立即更新（旧行为 C8）。提供一个单元测试，断言 `zh.json` 与 `en.json` 的键集合完全相同。
- 页面禁用浏览器默认的右键菜单和文本拖拽选择（输入框除外）；禁止页面缩放手势。
- `pnpm dev`：Vite 开发服务器，配合 `dotnet run --project tools/Chameleon.DevHost -- --port 5199 --token dev --allow-origin http://localhost:5173` 使用；在 `src/Chameleon.UI/README.md` 写明两条命令。
- Playwright 配置的 `webServer` 先 `pnpm build` 再启动开发宿主（`--ui dist --token e2e --delay 200`），测试访问 `http://127.0.0.1:5199/e2e/`。浏览器可执行文件路径可由环境变量 `PW_CHROMIUM_PATH` 指定。

### 验收标准

本文件开头「验证方式」中的五条前端命令全部通过；端到端用例至少包含：页面加载后显示应用外壳、能显示后端列表、语言切换生效。

---

## P2-T06 UI：单图流程

**依赖**：P2-T04、P2-T05

### 目标

实现 `legacy-inventory.md` 表 1.1 的 S1–S12，并修复 L5、L9、L10。

### 布局

```
┌──────────────────────────────────────────────┬────────────────────────┐
│  源图                                         │ [单图] [批量]      [⚙] │
│  （未截图时显示引导文字）                      │ 1. 截图                │
│                                              │   [截取当前视口]        │
├──────────────────────────────────────────────┤ 2. 服务商              │
│  结果                                         │   当前：柏拉图AI ▸ 设置 │
│  （未生成时显示占位文字；生成中显示进度）       │ 3. 生成参数            │
│                                              │   模型 / 风格模板       │
│                                              │   提示词 / 参考图       │
│                                              │   宽高比 / 图片尺寸     │
│                                              │   [生成]               │
│                                              │   [保存] [用作源图] [清空]│
├──────────────────────────────────────────────┴────────────────────────┤
│ 状态栏：状态文字 · 错误时显示错误文案与「详情」                          │
└───────────────────────────────────────────────────────────────────────┘
```

窗口最小宽度 900 像素；小于 1100 像素时右侧面板宽度固定 320 像素，左侧自适应。

### 实现要点

- 单图状态放在 `features/single/singleStore.ts`：`sourceAsset`、`resultAsset`、`prompt`、`styleId`、`aspectRatio`、`imageSize`、`referenceAsset`、`activeJobId`。
- 模型、宽高比、尺寸的可选项来自当前后端所选模型的 `capabilities`。
- 风格模板：选中后用模板提示词替换输入框内容；选 Custom 不改动输入框（修复 L10）。
- 参考图：单图模式也提供放置区（修复 L9），提交时作为 `style` 角色的参考。
- 生成：`jobs.submit`；进行中显示「取消」按钮（调用 `jobs.cancel`）；成功后显示结果；失败时状态栏显示按错误码本地化的文案，可展开查看 `detail`（修复 L5）；`not_ready` 时引导打开设置。
- 保存：选 JPEG 时先在 Canvas 转码并上传，再 `files.saveImage`；默认文件名 `AIRender_yyyyMMdd_HHmmss`。
- 点击任一图片进入全屏预览，Esc 或点击关闭。
- 「用作源图」「清空」与旧行为一致。

### 测试

- 单元测试：`singleStore` 的状态转换；风格模板替换规则。
- 端到端：截图 → 选模板 → 生成 → 结果出现；提示词含 `#fail:rate_limited` 时显示限流文案；`#slow` 时点击取消后状态为已取消；保存后开发宿主的输出目录里出现文件；用作源图；清空；预览开关。

---

## P2-T07 UI：批量流程

**依赖**：P2-T06

### 目标

实现 `legacy-inventory.md` 表 1.2 的 B1–B12，修复 L6、L12。

### 实现要点

- 状态放在 `features/batch/batchStore.ts`：`items[]`（`viewName`、`selected`、`sourceAsset`、`resultAsset`、`addonPrompt`、`status`、`jobId`、`error`）、`referenceAsset`、`groupId`、`running`。
- 「载入并截取命名视图」：`rhino.listNamedViews` 后逐个 `rhino.captureNamedView`，逐项更新缩略图与状态；没有命名视图时给出提示。
- 列表每行：勾选框、视图名、源图缩略图、结果缩略图、状态、追加提示词输入框、按钮（重新截图、重新生成、保存）。
- 「开始批量渲染」：对勾选且已截图的项调用 `jobs.submitBatch`，模式取设置中的 `batch.mode`（默认 `serial`）；每项 `label` 为视图名。
- 单项重新生成：`jobs.submit`，`dependsOn` 为其他已成功项的任务 id（旧行为 B7）。
- 取消：`jobs.cancel({ groupId })`，进行中的请求立即中断（修复 L6）。
- 进度：已完成数 / 总数。
- 风格参考图：拖入或点击选择（`files.pickImages`），支持 png、jpg、jpeg、webp；bmp 在 Canvas 中转为 PNG 后上传。可清除。
- 「全部保存」：`files.saveMany`，由宿主弹出文件夹选择对话框（修复 L12）。
- 共用提示词、模型、宽高比、尺寸与单图模式共享同一份参数面板组件。

### 测试

- 单元测试：`batchStore`；勾选逻辑；状态汇总。
- 端到端：载入三个命名视图 → 全部渲染 → 三项依次完成；中途取消后剩余项为已取消；某项失败不影响其余项且显示错误；单项重新生成；全选与全不选；全部保存后输出目录有三个以视图名命名的文件。

---

## P2-T08 UI：设置与服务商管理

**依赖**：P2-T06

### 目标

实现 `legacy-inventory.md` 表 1.3 的 C1–C9，修复 L7（Key 不再明文落盘，由 P1-T10 保证；UI 侧不回显）。

### 实现要点

- 设置以页面内的对话框呈现，不新开窗口。
- 服务商区域：下拉选择；显示 Base URL、接口格式、鉴权方式、模型列表；按钮：新增、编辑、删除（内置项显示「恢复默认」而不是删除）。
- Key 输入框：密码样式；已保存时显示占位「已保存」，输入新值后才调用 `providers.setKey`；提供清除按钮。
- 「获取 API Key」链接：`shell.openUrl`。
- 测试 Key：可选模型；调用 `providers.testKey`；显示成功，或按错误码显示失败原因。
- 新增与编辑对话框的字段与规则同旧行为 C5（选 openai 时自动填默认地址 `https://api.openai.com` 与模型 `gpt-image-2`，锁定鉴权方式为 bearer）；校验：名称、地址、至少一个模型；地址必须是 `http:` 或 `https:`。
- 语言切换：`settings.update({ language })`。
- 高级区域：系统提示词编辑与「恢复默认」；批量模式（P3-T11 才加入第二个选项，现在只显示当前值）；搜索增强开关（对应 `EnableSearchGrounding`，按服务商保存）。
- 关于区域：版本号、协议版本、日志目录路径。

### 测试

端到端：新增自定义服务商后出现在后端列表；编辑；删除当前服务商后回退到默认；设置 Key 后 `ready` 变为真；测试 Key 成功与失败（开发宿主为假后端提供可控结果）；语言切换后全界面文字变化；任何时候页面文本与网络响应中都不出现已保存的 Key。

---

## P2-T09 Rhino 宿主插件

**依赖**：P2-T04、P0-T3、P1-T10　**执行者**：agent 编写，人在 Rhino 中验证

### 目标

`src/Chameleon.Rhino`：把应用装进 Rhino。

### 涉及文件

- `Chameleon.Rhino.csproj`：`net7.0;net48`；`AssemblyName` 为 `Chameleon`；`TargetExt` 为 `.rhp`；`EnableDynamicLoading`；引用 `RhinoCommon` `8.19.25132.1001`（`ExcludeAssets="runtime"`）与 `Chameleon.App`。
- `ChameleonPlugin.cs`：插件类；程序集 GUID 沿用 `da1c6445-8347-41bf-8774-8c264cadf31d`；图标沿用 `legacy/EmbeddedResources/plugin-utility.ico`。
- `Commands/ChameleonCommand.cs`（`EnglishName` 为 `Chameleon`）与 `Commands/AIRenderCommand.cs`（`EnglishName` 为 `AIRender`，行为相同）。
- `UI/MainForm.cs`：Eto `Form`，内容为一个 `WebView`。
- `UI/MainWindowController.cs`：保证只有一个窗口实例；再次执行命令时激活已有窗口。
- `Hosting/RhinoHost.cs`：`IRhinoHost` 的实现。
- `Hosting/UiThread.cs`：`Task<T> InvokeAsync<T>(Func<T>)`，基于 `RhinoApp.InvokeOnUiThread`。
- `Hosting/ViewCapturer.cs`：截图逻辑。

### 实现要点

- 应用实例（`ChameleonApp`）在首次执行命令时创建，插件卸载或 Rhino 退出时释放。关闭窗口不停止服务器和任务，重新打开窗口后界面通过 `app.getState` 与 `jobs.list` 恢复。
- 前端产物嵌入：csproj 把 `..\Chameleon.UI\dist\**` 作为嵌入资源，逻辑名为 `ui/{相对路径}`；构建前检查 `dist/index.html` 存在，否则报错提示先构建前端；传入 `-p:SkipUi=true` 时嵌入一个占位页面，方便只验证 C# 编译。
- `MainForm`：`Owner` 为 `Rhino.UI.RhinoEtoApp.MainWindow`；默认 1280×820，最小 900×600；位置与尺寸用插件的 `Settings` 记住；`WebView.Url` 指向 `app.BaseUrl`；发布构建中 `BrowserContextMenuEnabled = false`；拦截 `OpenNewWindow` 与指向外部地址的 `DocumentLoading`，改用系统浏览器打开。
- `ViewCapturer`：
  - 活动视口：`ViewCapture`，尺寸取 `CaptureOptions` 或视口尺寸，`TransparentBackground = false`。
  - 命名视图：照搬 `legacy/Services/ScreenCapture.cs:113`–`157` 的相机保存与恢复逻辑（旧行为 B2），`finally` 中恢复。
  - `Bitmap` → `MemoryStream` 以 PNG 保存 → `ImageData.FromBytes`；及时释放 `Bitmap`。P0 报告若指出 macOS 需要别的编码方式，按 `spike-results.md` 的结论实现。
  - 没有活动文档或视口时抛 `RhinoHostException`，消息可直接展示。
- 对话框用 Eto 的 `SaveFileDialog`、`SelectFolderDialog`、`OpenFileDialog`，以 `MainForm` 为父窗口。
- `OpenUrl`：Windows 用 `ProcessStartInfo { UseShellExecute = true }`；macOS 用 `open` 命令。
- 日志：写到 `IAppPaths` 的日志目录，按天滚动，保留 7 天；错误同时 `RhinoApp.WriteLine` 一行摘要。
- `System.Drawing` 的使用仅限 `ViewCapturer` 与图标加载。
- 按 P0 报告处理键盘焦点问题（见 `phase-0-spikes.md` 决策表 D2、D6）。

### 验收标准

- `pnpm --dir src/Chameleon.UI build && dotnet build src/Chameleon.Rhino -c Release` 在 Linux 上成功，两个目标框架都产出 `Chameleon.rhp`。
- `dotnet build src/Chameleon.Rhino -c Release -p:SkipUi=true` 在没有 Node 的环境也成功。
- `Chameleon.Rhino` 中没有 `System.Windows`、`Microsoft.Win32` 的引用（用 `grep` 检查）。
- CI 增加作业：构建前端与插件，把 `net7.0` 与 `net48` 输出目录上传为构建产物。
- 人工：把构建产物装入 Windows 与 macOS 的 Rhino，执行 `Chameleon`，窗口出现并能完成一次截图。

---

## P2-T10 Yak 打包与发布流程

**依赖**：P2-T09　**执行者**：agent 编写，人完成首次发布

### 目标

产出可在 Rhino 包管理器中安装的包，并替换旧的发布工作流。

### 实现要点

- `packaging/manifest.yml`：`name: chameleon`、`version`（发布时由标签注入）、`authors`、`description`、`url`、`keywords`、`icon`。
- 包内目录采用 Rhino 8 的多目标布局：`manifest.yml` 与图标在根，`net7.0/` 与 `net48/` 子目录分别放对应框架的 `Chameleon.rhp` 及依赖。
- 新的 `.github/workflows/release.yml`（`v*` 标签触发）：
  1. 构建前端与插件。
  2. 组装包目录。
  3. 用 Yak 命令行执行 `yak build --platform any`，得到 `chameleon-{version}-rh8_19-any.yak`。Yak 命令行的获取方式以 McNeel 开发者文档「Package Manager Guides」当前的说明为准，在工作流里固定版本。
  4. 创建 GitHub Release，上传 `.yak` 以及两个框架各自的 zip。
  5. 仓库配置了 `YAK_TOKEN` 密钥时执行 `yak push`；未配置则跳过并在日志中说明。
- 发布说明模板：安装方式（包管理器搜索 Chameleon；或下载 `.yak` 拖入 Rhino；或下载 zip 拖入 `.rhp`）、最低版本要求 Rhino 8.19。
- 版本号来源统一：`Directory.Build.props` 的 `Version`、前端 `package.json`、`manifest.yml` 由标签驱动，写一个小脚本同步并在 CI 中校验一致。
- 更新根目录 `README.md`：功能、安装、系统要求、开发方式。

### 需要人做的事

- 确认包名 `chameleon` 在 Yak 服务器上可用，否则改名并同步 `manifest.yml`。
- 在本机用 `yak login` 获取令牌，写入仓库密钥 `YAK_TOKEN`。
- 首次发布建议先用 Yak 的测试服务器验证安装流程。

### 验收标准

推送一个预发布标签（如 `v2.0.0-beta.1`）后，Release 页面出现 `.yak` 与 zip；`.yak` 拖入 Rhino 8（Windows 与 macOS）可安装并加载。

---

## P2-T11 双平台人工验收

**依赖**：P2-T07、P2-T08、P2-T10　**执行者**：human

在 Windows 与 macOS 上各执行一遍，结果记录到 `docs/plan/acceptance-p2.md`（每条：通过 / 不通过 / 备注）。

1. `legacy-inventory.md` 第 1 节的全部条目（S1–S12、B1–B12、C1–C9、O1–O2）。
2. 旧版用户升级：Windows 上保留旧的 `%APPDATA%\AIRenderer\settings.json`，首次启动新版后服务商、Key、语言、所选模型都还在；新的 `settings.json` 中找不到 Key 明文。
3. 错误可见：填错误的 Key 生成，界面显示鉴权失败；断网生成，界面显示网络错误。
4. 取消：生成过程中点取消，立即停止。
5. 关闭窗口再打开，进行中的任务与已有结果仍在。
6. 中文输入法输入提示词；复制粘贴；拖入参考图。
7. 4K 尺寸生成一次，界面无明显卡顿，结果可保存。
8. Rhino 8 以 .NET Framework 方式启动（Windows）时插件可加载。
9. 有 Rhino 9 的话重复第 1、6 条。

发现的问题登记为新的任务，修复后重新验收对应条目。

---

## P2-T12 删除旧 WPF 代码

**依赖**：P2-T11 全部通过

- 删除 `legacy/` 目录与 CI 中的 `legacy` 作业。
- 删除 `spikes/` 与 `.github/workflows/spike.yml`。
- `legacy-inventory.md` 保留，顶部加一行说明：旧代码已删除，可在标签 `legacy-final` 处查看。删除前先打这个标签。
- 更新 `AGENTS.md` 的「当前状态」。

### 验收标准

通用命令与前端五条命令通过；仓库中 `grep -r "System.Windows" --include=*.cs` 无结果。
