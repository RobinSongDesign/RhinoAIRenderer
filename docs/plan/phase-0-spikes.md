# P0 技术验证

目的：在投入 UI 开发之前，用一个一次性的小插件在真实的 Rhino（Windows 与 macOS）里确认几项关键假设。
agent 负责写插件，人负责在两个平台上各运行一次并提交报告。

已经确认、不需要再验证的事项：

- RhinoCommon 8.19 起自带的 Eto 提供 `WebView.MessageReceived`（对应 JS 端 `window.eto.postMessage(string)`）、`ExecuteScriptAsync`、`LoadHtml(html, baseUri)`、`DocumentLoading`（可取消导航）。
- 引用 RhinoCommon 的 `net7.0;net48` 工程可以在 Linux 上用 .NET 8 SDK 编译。
- Rhino 9 的 RhinoCommon 包目标框架为 `net8.0` 与 `net48`。

---

## P0-T1 验证插件 `ChameleonSpike`

**执行者**：agent　**依赖**：无

### 目标

提供命令 `ChameleonSpike`：打开一个带 WebView 的 Eto 窗口，自动跑完全部可自动化的检查，在页面上显示结果，并把报告写到用户桌面的 `chameleon-spike-report.json`。需要人工操作的检查在页面上给出操作提示和「通过 / 不通过」按钮。

### 涉及文件

- 新建 `spikes/ChameleonSpike/ChameleonSpike.csproj`：`net7.0;net48`，`TargetExt` 为 `.rhp`，`EnableDynamicLoading` 为 `true`，引用 `RhinoCommon` `8.19.25132.1001`（`ExcludeAssets="runtime"`）和 `Newtonsoft.Json` `13.0.3`。使用新的插件 GUID，不要与正式插件相同。
- 新建 `spikes/ChameleonSpike/` 下的源文件：插件类、命令、`SpikeForm`（Eto）、`SpikeServer`（`HttpListener`）、各检查项、报告模型。
- 新建 `spikes/ChameleonSpike/web/index.html`：单文件页面（内联 JS 与 CSS，不引入构建工具），作为嵌入资源。
- 新建 `spikes/ChameleonSpike/README.md`：安装与运行步骤（把 `.rhp` 拖入 Rhino，执行 `ChameleonSpike`）。

这个工程不加入之后的 `Chameleon.sln`，不依赖 `src/` 下的任何代码。

### 检查项

每项记录：`id`、`passed`（`true` / `false` / `null` 表示未执行）、`detail`（数值或错误信息）、`durationMs`。

| ID | 检查内容 | 方式 | 通过标准 |
|---|---|---|---|
| A1 | `HttpListener` 能在 `http://127.0.0.1:{随机端口}/` 上启动 | 自动 | 启动成功 |
| A2 | WebView 能加载 `http://127.0.0.1:{port}/{token}/index.html` | 自动（页面加载后回报） | 页面脚本执行 |
| A3 | 页面 `fetch` POST JSON 到 `/rpc` 并取回响应 | 自动 | 往返成功，记录耗时 |
| A4 | 页面通过 `EventSource` 收到服务器推送 | 自动 | 5 秒内收到 3 条 |
| A5 | 页面用 `<img>` 加载服务器返回的 3840×2160 PNG | 自动 | `onload` 触发，记录耗时 |
| A6 | 页面 `fetch` POST 20 MB 二进制到服务器 | 自动 | 服务器收到的字节数与哈希一致 |
| A7 | 用系统浏览器访问同一地址但令牌错误 | 自动（宿主用 `HttpClient` 模拟） | 返回 404 |
| B1 | `window.eto.postMessage` → `MessageReceived` | 自动 | 宿主收到并回应 |
| B2 | `ExecuteScriptAsync` 返回值 | 自动 | 取回 `"2"`（`1+1`） |
| B3 | 消息通道传输 1 MB、10 MB 字符串 | 自动 | 成功与否、耗时 |
| B4 | `LoadHtml(html, baseUri)` 加载缓存目录中的页面，并用 `file://` 显示同目录图片 | 自动（第二个 WebView） | 图片 `onload` 触发 |
| C1 | `ViewCapture.CaptureToBitmap` 截取活动视口，`Bitmap.Save(stream, ImageFormat.Png)` 得到字节 | 自动 | 字节非空，PNG 头正确，记录尺寸与耗时 |
| C2 | 指定尺寸截图：1920×1080、3840×2160 | 自动 | 输出尺寸与请求一致 |
| C3 | 截取命名视图并恢复相机（逻辑照搬 `ScreenCapture.CaptureNamedView`） | 自动（文档中存在命名视图时） | 截图成功，相机参数恢复到截图前 |
| C4 | 从线程池线程经 `RhinoApp.InvokeOnUiThread` 触发截图 | 自动 | 成功，无死锁 |
| D1 | 在页面的 `<textarea>` 中输入英文 | 人工 | 能输入 |
| D2 | 在页面中用中文输入法输入 | 人工 | 候选框正常，上屏正确 |
| D3 | 复制、粘贴、全选快捷键 | 人工 | 可用，且不会触发 Rhino 命令 |
| D4 | 把图片文件从访达 / 资源管理器拖到页面的放置区 | 人工 | 页面收到文件（`drop` 事件，`File` 可读） |
| D5 | `<input type="file">` 弹出系统文件选择 | 人工 | 可选择并读取 |
| D6 | 焦点在页面输入框时按 Esc、空格、回车 | 人工 | 不会被 Rhino 当作命令行输入 |
| E1 | Windows：`ProtectedData.Protect/Unprotect` 往返 | 自动 | 一致 |
| E2 | macOS：钥匙串写入、读取、删除（Security.framework P/Invoke） | 自动 | 一致；记录是否弹出授权对话框 |
| F1 | Eto `SaveFileDialog`、`SelectFolderDialog`、`OpenFileDialog` | 人工 | 能弹出并返回路径 |
| F2 | 用系统默认浏览器打开 `https://www.rhino3d.com` | 人工 | 浏览器打开 |
| G1 | 对象蒙版原型：选中若干物体后，输出一张与 C1 同尺寸的黑白图（选中物体可见部分为白，其余为黑） | 自动 + 人工目测 | 报告保存两张图到桌面；人工确认轮廓与遮挡关系正确 |
| H1 | 记录 `navigator.userAgent` | 自动 | 记录 |
| H2 | 页面创建 4096×4096 Canvas 并 `toBlob('image/png')` | 自动 | 成功 |
| H3 | `navigator.clipboard.write` 写入图片 | 人工触发 | 记录是否可用 |
| H4 | 页面 `devicePixelRatio`、窗口缩放后布局 | 自动 | 记录 |

### 实现要点

- G1 的建议做法：注册一个 `DisplayConduit`，在 `PreDrawObject` 中接管绘制，把选中物体用纯白自发光材质、未选中物体用纯黑材质绘制（保留深度测试以得到正确遮挡），背景填充黑色，然后用与 C1 相同的尺寸截图，截完立即注销 conduit。若此路不通，记录失败原因并尝试备选：临时切换到自定义显示模式。两种都失败不算阻塞，如实记录。
- E2 的 P/Invoke 目标：`/System/Library/Frameworks/Security.framework/Security` 中的 `SecKeychainAddGenericPassword`、`SecKeychainFindGenericPassword`、`SecKeychainItemDelete`。服务名 `ChameleonSpike`。
- 平台判断用 `Rhino.Runtime.HostUtils.RunningOnOSX`。
- 所有检查互相独立，单项异常要捕获并记入报告，不能中断其余检查。
- 报告顶部记录：平台、操作系统版本、Rhino 版本（`RhinoApp.Version`）、.NET 运行时（`RuntimeInformation.FrameworkDescription`）、插件目标框架。
- 窗口以 `Rhino.UI.RhinoEtoApp.MainWindow` 为 `Owner`。

### 验收标准

- `dotnet build spikes/ChameleonSpike/ChameleonSpike.csproj -c Release` 在 Linux 上成功，产出 `bin/Release/net7.0/ChameleonSpike.rhp` 与 `bin/Release/net48/ChameleonSpike.rhp`。
- 新增 GitHub Actions 工作流 `.github/workflows/spike.yml`：手动触发，构建并把两个框架的输出目录分别打成 zip 上传为构建产物，供人下载。
- 代码中没有平台专属的 UI 类型（WPF、WinForms、AppKit）。

### 不在范围内

不做任何产品功能；不追求界面美观；不写单元测试。

---

## P0-T2 运行并提交报告

**执行者**：human　**依赖**：P0-T1

1. 从 Actions 下载构建产物。Windows 用 `net7.0` 版本；另在 Rhino 8 以 `/netfx` 方式启动时用 `net48` 版本再跑一次自动项。macOS 用 `net7.0` 版本。如有 Rhino 9，也各跑一次。
2. 打开任意带几个物体和至少一个命名视图的模型，选中两三个物体，执行 `ChameleonSpike`。
3. 按页面提示完成人工检查项。
4. 把桌面上的 `chameleon-spike-report.json` 以及 G1 的两张图提交到 `docs/plan/spike-results/`，文件名带平台与 Rhino 版本，例如 `mac-rhino8.36.json`。

---

## P0-T3 根据报告更新决策

**执行者**：agent　**依赖**：P0-T2

读取 `docs/plan/spike-results/` 下的报告，写出 `docs/plan/spike-results.md`（各平台结果对照表与结论），并按下表更新 `architecture.md` 第 8 节的 ADR 状态和受影响的任务文档。

| 结果 | 结论 | 需要修改 |
|---|---|---|
| A1–A6 在两个平台全部通过 | ADR-02 定为「采用」，不实现备选传输 | 无 |
| A2–A6 在 macOS 失败，B1–B4 通过 | 采用 ADR-03；P2-T02 增加 `EtoMessageTransport`，P2-T05 增加对应的前端 `Transport` 实现，P2-T09 用 `LoadHtml` 加载 | `phase-2-ui.md` 中 P2-T02、P2-T05、P2-T09 |
| A 与 B 在 macOS 都失败 | ADR-01 不成立，改为纯 Eto 原生界面 | 重写 `phase-2-ui.md`、`phase-3-editing.md`，先停下来向项目负责人确认 |
| D2（中文输入法）失败 | 提示词输入改用 Eto 原生文本框，覆盖在 WebView 对应区域上方 | P2-T06、P2-T09 |
| D4（拖放）失败 | 拖放区退化为点击选择文件 | P2-T07 |
| D6 失败（按键被 Rhino 截获） | 窗口获得焦点期间在宿主侧拦截按键转发 | P2-T09 |
| C1 或 C2 在 macOS 失败 | 换用像素锁定方式自行编码 PNG，或经 `Eto.Drawing.Bitmap` 转存 | P2-T09 |
| E2 失败或每次都弹授权框 | macOS 使用加密文件存储 | P1-T10 |
| G1 失败 | P3-T07 改为「按图层 / 按材质着色截图后在 UI 中点选颜色区域」的替代方案，或推迟 | `phase-3-editing.md` 中 P3-T07 |
| H2 失败 | 编辑器的最大工作分辨率降到实测可用值 | P3-T04 |

P1 不依赖 P0 的结果，可以与 P0 同时进行。P2-T09 必须在 P0-T3 之后开始。
