# Chameleon（Rhino AI Renderer）

> **重构进行中。** 跨平台的新版本（2.x）代码已基本完成，正在等待在真实 Rhino 中验证；当前可用的仍是 WPF 版本（1.x，仅 Windows，代码在 [`legacy/`](legacy/)）。计划与进度见 [`docs/plan/`](docs/plan/README.md)，开发约定见 [`AGENTS.md`](AGENTS.md)。

Chameleon 是一个 Rhino 插件：截取视口画面，调用图像生成模型做 AI 渲染与图片编辑。

<img width="1737" height="1090" alt="Chameleon" src="https://github.com/user-attachments/assets/1532692d-f689-4a62-8964-c07aaa9a598e" />

## 功能（2.x）

- **单图**：截取当前视口，选择风格模板或输入提示词，生成、取消、保存（PNG / JPEG）、用结果继续生成。
- **批量**：读取文档的全部命名视图，一次渲染；后面的视图参考前面的结果，保持光照与材质一致；可单独重新生成某个视图。
- **风格参考图**：拖入或选择一张图片，单图与批量都可用。
- **两种接入方式**：使用自己的 API Key（Google Gemini 原生、OpenAI 兼容接口、各类中转服务），或登录 Chameleon 账号使用（开发中）。
- **API Key 安全保存**：Windows 用 DPAPI，macOS 用钥匙串，不再明文写入配置文件。
- 中文 / English 界面，随时切换。

## 系统要求

- Rhino 8.19 或更高版本（含 Rhino 9），Windows 或 macOS。
- Windows 需要 WebView2 运行时（Windows 11 自带）。

## 安装

从 [Releases](https://github.com/RobinSongDesign/RhinoAIRenderer/releases) 下载：

- `.yak`：拖入 Rhino 窗口即可安装；发布到 Rhino 包管理器后也可在 `PackageManager` 中搜索 Chameleon。
- `Chameleon-<版本>-net7.0.zip`：解压后把 `Chameleon.rhp` 拖入 Rhino 8 / 9。
- `Chameleon-<版本>-net48.zip`：Windows 上以 `/netfx` 方式启动的 Rhino 8 使用。

安装后执行命令 `Chameleon`（旧命令 `AIRender` 仍然可用）。从 1.x 升级时，原有的服务商、API Key、语言和模型选择会自动迁移。

## 结构

| 目录 | 内容 |
|---|---|
| `src/Chameleon.Core` | 与界面、Rhino 都无关的核心：服务商适配（Gemini、OpenAI Images）、提示词、任务队列、设置 |
| `src/Chameleon.App` | 应用层：本地桥接服务器、RPC 服务、资源与密钥存储 |
| `src/Chameleon.UI` | Web 前端（React + TypeScript），运行在 Rhino 的 WebView 中 |
| `src/Chameleon.Rhino` | Rhino 插件：命令、窗口、视口截图 |
| `tools/Chameleon.DevHost` | 脱离 Rhino 运行整个应用（假 Rhino、假后端），用于界面开发与端到端测试 |
| `tools/Chameleon.Cli` | 命令行冒烟工具，用真实 Key 直接调用服务商 |
| `legacy/` | 1.x WPF 版本（冻结） |

## 开发

需要 .NET 8 SDK、Node.js 22、pnpm。

```bash
dotnet test Chameleon.sln -c Release -p:SkipUi=true     # .NET 单元测试
pnpm --dir src/Chameleon.UI install
pnpm --dir src/Chameleon.UI test                         # 前端单元测试
pnpm --dir src/Chameleon.UI e2e                          # 端到端测试（启动开发宿主）
pnpm --dir src/Chameleon.UI build && dotnet build src/Chameleon.Rhino -c Release   # 插件
```

不开 Rhino 调试界面的方法见 [`src/Chameleon.UI/README.md`](src/Chameleon.UI/README.md)。

## 发布

推送 `v2.x.y` 标签（如 `v2.0.0-beta.1`）由 `.github/workflows/release.yml` 构建并发布；`v1.*` 标签仍发布旧的 WPF 版本。版本号由 `tools/scripts/set_version.py` 从标签同步到各处。
