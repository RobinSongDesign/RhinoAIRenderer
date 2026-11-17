# Agent 工作守则

本文件面向在这个仓库里工作的 AI agent 和开发者。开始任何任务之前先读完本文件，再读 `docs/plan/README.md` 与 `docs/plan/architecture.md`。

## 项目是什么

Chameleon 是一个 Rhino 插件：截取视口画面，调用图像生成模型做 AI 渲染与图片编辑。
仓库正在按 `docs/plan/` 中的计划重构，目标是同时支持 Windows 与 macOS、支持自带 API Key 与账号登录两种接入方式，并完善图片编辑功能。

## 当前状态

> 执行 P1-T01、P2-T12 等改变仓库结构的任务时，更新这一节。

- 旧的 WPF 单体插件已原样移入 `legacy/`，处于冻结状态，只能在 Windows 上构建（CI 的 `legacy` 作业负责验证）。不要修改它的代码。
- 新代码位于 `src/`（`Chameleon.Core`、`Chameleon.App`）、`tests/`、`tools/`，解决方案为 `Chameleon.sln`。
- 各任务的完成情况见 `docs/plan/README.md` 的任务表。

## 环境与命令

需要 .NET 8 SDK、Node.js 22、pnpm。Ubuntu 24.04 上安装 .NET：

```bash
apt-get update && apt-get install -y dotnet-sdk-8.0
```

常用命令：

```bash
# .NET: build and test everything except the legacy WPF project
dotnet build Chameleon.sln -c Release
dotnet test  Chameleon.sln -c Release

# Front end (available after P2-T05)
pnpm --dir src/Chameleon.UI install
pnpm --dir src/Chameleon.UI typecheck
pnpm --dir src/Chameleon.UI lint
pnpm --dir src/Chameleon.UI test
pnpm --dir src/Chameleon.UI build
pnpm --dir src/Chameleon.UI e2e

# Run the app without Rhino (available after P2-T03)
dotnet run --project tools/Chameleon.DevHost -- --port 5199 --token dev --ui src/Chameleon.UI/dist
```

环境限制：

- `legacy/` 下的 WPF 工程在 Linux 和 macOS 上无法构建，只能通过 GitHub Actions 的 Windows 作业验证。不要为了让它在本地编译而修改它。
- 引用 RhinoCommon 的工程可以在任何系统上**编译**，但无法运行。凡是需要在 Rhino 里确认的行为，任务文档里标注了「人工验收」，在最终回复中明确列出哪些部分未经运行验证。
- 没有真实 API Key 时不要伪造接口响应来声称验证通过；用测试夹具做单元测试，并说明真实接口未验证。

## 任务流程

1. 在 `docs/plan/README.md` 的任务表中确认任务的依赖都已完成。
2. 读该任务所在的阶段文档，以及它引用的规格文档。
3. 只做任务范围内的事。发现范围外的问题，记录在最终回复里，不顺手修改。
4. 实现并补齐测试，运行任务的验收命令。
5. 把任务表里该任务的状态改为 `done`；计划与实际不一致的地方同步修改文档。
6. 最终回复包含：改动文件列表、验收命令的实际输出、未验证的部分、偏离计划之处及原因。

## 架构约束

依赖方向：`Chameleon.Rhino` → `Chameleon.App` → `Chameleon.Core`。前端只通过桥接协议访问宿主。

| 规则 | 说明 |
|---|---|
| Core 与 App 不引用 RhinoCommon、Eto、`System.Drawing`、任何 UI 框架 | 这两个程序集必须能在 Linux 上测试 |
| .NET 侧不解码像素 | 图片是不透明的编码字节（`ImageData`）；裁剪、合成、缩放、蒙版都在前端 Canvas 中做 |
| Rhino API 只在 `Chameleon.Rhino` 中调用，且在 Rhino UI 线程上 | 通过 `IRhinoHost` 暴露给上层 |
| 前端不直接请求服务商或云端，不持有 API Key | Key 只有写入方向，任何接口都不返回已保存的 Key |
| 新增服务商协议 = 新增一个 `IProviderAdapter` | 不在别处写协议相关的分支 |
| 提示词文案集中在 `PromptComposer` 与 `PromptTexts` | 不在适配器或 UI 里拼接提示词 |
| 桥接协议的每次变更同步更新 `docs/plan/bridge-protocol.md`、C# DTO、`types.ts`、开发宿主 | 四处保持一致 |

## 代码规范

### 通用

- 代码注释一律用英文，写清楚「为什么」，不复述代码。公开类型与公开成员写 XML 文档注释（C#）或 TSDoc（TypeScript）。
- 面向对象、低耦合：依赖通过构造函数注入接口；一个类只有一个变化原因；不使用全局可变状态和静态服务定位。
- 优先复用已有的类型和工具函数；新增公共工具前先搜索是否已有。
- 不留注释掉的代码、未使用的成员、`TODO` 之外的占位实现。确实要留的 `TODO` 写明对应的任务 ID。
- 面向用户的文字全部走国际化，中英文同时提供。
- 文档（`docs/`）用中文；代码标识符、注释、提交说明用英文。

### C#

- C# 10，开启 `Nullable`，`src/` 下警告视为错误。
- `netstandard2.0` 工程中不使用 `record`、`init`、`System.Text.Json`、`Span` 相关的新 API；JSON 用 Newtonsoft.Json。
- 所有 I/O 都是异步的，接收并向下传递 `CancellationToken`。禁止 `.Result`、`.Wait()`、`GetAwaiter().GetResult()`；除事件处理器外禁止 `async void`。库代码中 `await` 使用 `ConfigureAwait(false)`。
- 共享一个 `HttpClient`；请求头设在 `HttpRequestMessage` 上，不修改 `DefaultRequestHeaders`。
- 失败用异常表达（面向用户的失败用 `RenderException`），不用返回 `null` 表示错误。取消用 `OperationCanceledException`。
- 实现 `IDisposable` 的对象及时释放；大的字节数组避免不必要的拷贝（流式序列化、`ArraySegment`）。
- 命名：类型与公开成员 PascalCase，私有字段 `_camelCase`，接口以 `I` 开头，异步方法以 `Async` 结尾。一个文件一个公开类型，文件名与类型名一致。

### TypeScript

- `strict` 模式；不使用 `any`（确有必要时用 `unknown` 加类型守卫）。
- 函数式组件与 hooks；状态用 Zustand store，组件内只放纯界面状态。
- 与宿主的通信只经过 `src/bridge/`；组件不直接调用 `fetch`。
- 像素与几何计算写成不依赖 DOM 的纯函数，便于单元测试。
- 不引入大型 UI 组件库或图形库；新增依赖需在最终回复中说明理由。
- 兼容 Safari 15 与 Chrome 100：不使用它们不支持的语法与 API。

## 测试要求

- 新增或修改的逻辑必须有测试。修复缺陷时先写能复现的测试。
- 测试不访问外部网络。HTTP 用自定义 `HttpMessageHandler` 或开发宿主的假后端；时间用可注入的时钟或延迟函数。
- 测试夹具（样例图片、请求与响应的 JSON）放在各测试工程的 `Fixtures/` 目录，单个文件不超过 200 KB。
- 端到端测试对着开发宿主运行，不依赖 Rhino。
- 不为了通过而跳过、删除或放宽已有测试。

## 安全要求

- 不把任何 Key、令牌、密码写入代码、测试夹具、日志、提交说明或文档。测试中使用明显的假值（如 `test-key-123`）。
- 日志与错误信息在输出前经过 `Redactor` 脱敏。
- 本地服务器只绑定 `127.0.0.1`，校验路径令牌、`Host` 与 `Origin`。
- `shell.openUrl` 等会触发外部动作的入口只接受 `https:` 地址。
- 不保存用户的源图与提示词到本机以外的任何地方，除非是用户发起的生成请求本身。

## 提交

- 提交说明用英文祈使句，首行不超过 72 个字符，说明做了什么；正文说明原因与影响。一个任务可以有多个提交，每个提交都应能构建。
- 不提交构建产物（`bin/`、`obj/`、`dist/`、`node_modules/`、`*.rhp`、`*.yak`）。
- 未经项目负责人要求，不创建 Pull Request，不推送到任务指定分支以外的分支，不打发布标签。
