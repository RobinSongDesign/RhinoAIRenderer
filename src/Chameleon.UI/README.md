# Chameleon.UI

Chameleon 的 Web 前端。插件运行时，它在 Rhino 的 Eto WebView 中加载，通过本地回环桥接与宿主通信（协议见 [`docs/plan/bridge-protocol.md`](../../docs/plan/bridge-protocol.md)）。

技术栈：React 19、TypeScript（strict）、Vite、Zustand、Vitest、Playwright。不使用 UI 组件库，样式为 CSS 变量加 CSS Modules。构建目标为 Chrome 100 与 Safari 15（WebView2 与 WKWebView）。

## 目录

```
src/
  bridge/      与宿主通信的唯一入口：协议类型、传输层、类型化客户端
  state/       Zustand store（应用状态、任务、界面状态）与服务上下文
  i18n/        zh.json / en.json 与翻译函数
  components/  通用组件
  imaging/     Canvas 相关的图片处理
  features/    各功能区：shell、single、batch、settings、params
e2e/           Playwright 端到端测试（对开发宿主运行）
```

## 常用命令

```bash
pnpm install
pnpm typecheck     # TypeScript
pnpm lint          # ESLint
pnpm test          # Vitest 单元测试
pnpm build         # 产物输出到 dist/，由插件以嵌入资源的形式提供
pnpm e2e           # 构建后启动开发宿主并运行 Playwright
```

`pnpm e2e` 需要 .NET 8 SDK（用于启动 `tools/Chameleon.DevHost`）和 Playwright 的 Chromium。若浏览器不在默认位置，用环境变量 `PW_CHROMIUM_PATH` 指定。

## 开发

两个终端：

```bash
# 1. 开发宿主：真实的应用层 + 假 Rhino + 假后端，允许 Vite 开发服务器跨域调用
dotnet run --project tools/Chameleon.DevHost -- --port 5199 --token dev --allow-origin http://localhost:5173 --fake-providers

# 2. Vite 开发服务器
VITE_BRIDGE_URL=http://127.0.0.1:5199/dev/ pnpm --dir src/Chameleon.UI dev
```

浏览器打开 http://localhost:5173 。开发宿主的约定：

- 假后端 `fake` 总是可用；提示词含 `#fail:rate_limited`（任意错误码）时失败，含 `#slow` 时耗时 10 秒。
- 加上 `--fake-providers` 后，内置服务商的请求在本地应答，唯一被接受的 Key 是 `dev-good-key`。
- 保存类对话框直接写入 `--out` 指定的目录（默认在数据目录下的 `out`）。

## 约定

- 组件不直接调用 `fetch` 或 `EventSource`（ESLint 会报错），一律通过 `useBridge()` 或 store。
- 所有可见文字都走 `useT()`，`zh.json` 与 `en.json` 的键必须一致（有测试检查）。
- 像素与几何计算写成不依赖 DOM 的纯函数，放在可单元测试的模块里。
