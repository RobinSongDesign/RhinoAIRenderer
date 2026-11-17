# ChameleonSpike

阶段 0 的一次性验证插件。它在真实的 Rhino 里检查新架构依赖的几项平台能力（回环 HTTP、Eto 消息通道、视口截图、密钥存储、对象蒙版、WebView 行为），并把结果写成报告。检查项的含义与结论的用法见 [`docs/plan/phase-0-spikes.md`](../../docs/plan/phase-0-spikes.md)。

这个工程不属于 `Chameleon.sln`，也不依赖 `src/` 下的代码。

## 获取

- 从 GitHub Actions 的 **Spike build** 工作流（手动触发）下载构建产物；或者
- 本地构建：`dotnet build spikes/ChameleonSpike -c Release`，产物在 `bin/Release/net7.0/` 与 `bin/Release/net48/`。

| 运行环境 | 使用哪个版本 |
|---|---|
| Rhino 8 / 9，Windows（默认的 .NET Core 模式） | `net7.0` |
| Rhino 8，Windows，以 `/netfx` 启动 | `net48` |
| Rhino 8 / 9，macOS | `net7.0` |

最低版本 Rhino 8.19。

## 运行

1. 打开一个带若干物体、至少一个命名视图的模型，选中两三个物体。
2. 把对应目录里的 `ChameleonSpike.rhp` 拖入 Rhino 窗口（同目录下的 `.dll` 要保留在一起）。
3. 执行命令 `ChameleonSpike`。
4. 等待自动检查跑完（表格中不再有 `…`），然后按页面下半部分的提示完成人工检查，逐项点 Pass 或 Fail。
5. 关闭窗口。报告写在桌面的 `chameleon-spike-report.json`，对象蒙版检查另存 `chameleon-spike-beauty.png` 与 `chameleon-spike-mask.png`。
6. 把这三个文件提交到 `docs/plan/spike-results/`，文件名加上平台与 Rhino 版本，例如 `mac-rhino8.36.json`。

## 说明

- 页面首先通过 `http://127.0.0.1` 加载。8 秒内没有加载成功时，A2 记为失败，并改用 `LoadHtml` 直接载入页面，以便继续评估 Eto 消息通道（B 组）。这种情况下 A3–A6 记为跳过。
- E2 会在登录钥匙串里写入并立即删除一条名为 `ChameleonSpike` 的记录。如果 macOS 弹出授权对话框，请在报告的备注里写明。
- G1 需要先选中物体，否则记为跳过。
