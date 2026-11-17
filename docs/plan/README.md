# Chameleon 重构与扩展开发计划

本目录是 Chameleon（原 Rhino AIRenderer）下一阶段的完整开发计划，写给负责执行的 agent 和人。
每个任务都自带目标、涉及文件、实现要点和验收标准，可以单独派发。

## 目标

1. **跨平台**：同一个插件在 Rhino 8 / 9 的 Windows 和 macOS 上运行。
2. **两种接入方式**：用户自带 API Key 直连服务商；或登录账号，经由我们自己的云端网关使用（由我们分发点数）。
3. **完善图片编辑**：编辑历史、局部重绘、Rhino 对象蒙版、多候选对比、多参考图、一键操作。

## 文档索引

| 文件 | 内容 | 谁需要读 |
|---|---|---|
| [`architecture.md`](architecture.md) | 目标架构、仓库布局、分层规则、核心接口、数据模型、技术决策记录 | 所有人，先读 |
| [`legacy-inventory.md`](legacy-inventory.md) | 现有代码的功能清单、必须保留的行为、已知缺陷、旧配置格式 | P1、P2 |
| [`bridge-protocol.md`](bridge-protocol.md) | UI 与宿主之间的协议：传输、RPC 方法、事件、DTO | P2、P3、P4 的 UI 相关任务 |
| [`cloud-api.md`](cloud-api.md) | 云端网关的 REST API、数据库表、计费规则 | P4 |
| [`phase-0-spikes.md`](phase-0-spikes.md) | 技术验证 | P0 |
| [`phase-1-core.md`](phase-1-core.md) | 核心库 | P1 |
| [`phase-2-ui.md`](phase-2-ui.md) | 跨平台宿主与 UI，功能对齐现有版本 | P2 |
| [`phase-3-editing.md`](phase-3-editing.md) | 图片编辑功能 | P3 |
| [`phase-4-cloud.md`](phase-4-cloud.md) | 云端账号与分发 | P4 |
| [`phase-5-backlog.md`](phase-5-backlog.md) | 后续候选项，未排期 | 规划时 |
| `spike-results.md` | P0 的实测结果（P0-T2 产出，现在还不存在） | P2 开始前 |

仓库根目录的 [`AGENTS.md`](../../AGENTS.md) 是 agent 的工作守则（构建命令、代码规范、分层约束），每个任务开始前都要读。

## 已采用的默认决策

下面四项在写计划时没有得到明确答复，按推荐方案写入计划。要改的话先改 `architecture.md` 第 8 节的对应记录，再调整受影响的任务。

| # | 决策 | 默认值 | 影响范围 |
|---|---|---|---|
| D1 | UI 技术路线 | Eto 窗口 + WebView 承载 Web 前端 | P2、P3 全部 UI 任务 |
| D2 | 支持的 Rhino 版本 | Rhino 8.19 及以上、Rhino 9；放弃 Rhino 7 | 目标框架、打包 |
| D3 | 主要用户区域 | 以中国大陆用户为主，服务器放香港或新加坡 | P4 部署、登录方式、支付 |
| D4 | 阶段顺序 | P0 → P1 → P2 → P3，P4 的服务端部分在 P1 之后可并行 | 排期 |

## 阶段总览

| 阶段 | 内容 | 产出 | 能否在 Linux 云端完整验证 |
|---|---|---|---|
| P0 | 技术验证 | 验证插件 + 实测报告 + 路线确认 | 只能编译；运行需要人在 Rhino 里操作 |
| P1 | 核心库 `Chameleon.Core` | 服务商适配、任务队列、设置、密钥存储、命令行冒烟工具 | 能 |
| P2 | 宿主与 UI | 功能与现有版本对齐的跨平台插件，Yak 包 | UI 和桥接能；Rhino 内行为需人工验收 |
| P3 | 编辑功能 | 历史树、蒙版、对象蒙版、多候选、多参考 | UI 能；Rhino 对象蒙版需人工验收 |
| P4 | 云端账号 | 服务端、登录、点数、兑换码、管理后台 | 能（服务端用 Docker 起依赖） |
| P5 | 待排期 | 支付、模板库、团队、多通道输入等 | — |

## 任务清单与状态

状态取值：`todo`、`doing`、`done`、`blocked`。执行任务的 agent 在自己的提交里更新这一列。
「执行者」一列中 `agent` 表示可完全交给 agent，`human` 表示必须由人操作，`agent+human` 表示 agent 写完后需要人在 Rhino 里验收。

### P0 技术验证 — 见 [`phase-0-spikes.md`](phase-0-spikes.md)

| ID | 任务 | 依赖 | 执行者 | 状态 |
|---|---|---|---|---|
| P0-T1 | 验证插件 `ChameleonSpike` | — | agent | todo |
| P0-T2 | 在 Windows 和 macOS 的 Rhino 中运行并提交报告 | P0-T1 | human | todo |
| P0-T3 | 根据报告更新决策记录 | P0-T2 | agent | todo |

### P1 核心库 — 见 [`phase-1-core.md`](phase-1-core.md)

| ID | 任务 | 依赖 | 执行者 | 状态 |
|---|---|---|---|---|
| P1-T01 | 仓库重组、解决方案骨架、CI | — | agent | done |
| P1-T02 | 图像基础类型与格式探测 | P1-T01 | agent | done |
| P1-T03 | 生成请求、结果、能力、错误模型、`IRenderBackend` | P1-T02 | agent | done |
| P1-T04 | 服务商配置与内置目录 | P1-T03 | agent | done |
| P1-T05 | 提示词组合器 | P1-T03 | agent | done |
| P1-T06 | Gemini 适配器 | P1-T04、P1-T05 | agent | done |
| P1-T07 | OpenAI Images 适配器 | P1-T04、P1-T05 | agent | done |
| P1-T08 | `DirectBackend` 与 HTTP 管线 | P1-T06、P1-T07 | agent | done |
| P1-T09 | 设置存储与旧配置迁移 | P1-T04 | agent | todo |
| P1-T10 | 密钥存储的平台实现 | P1-T09 | agent | todo |
| P1-T11 | 任务队列与批量计划 | P1-T03 | agent | todo |
| P1-T12 | 命令行冒烟工具 | P1-T08 | agent | todo |

### P2 宿主与 UI — 见 [`phase-2-ui.md`](phase-2-ui.md)

| ID | 任务 | 依赖 | 执行者 | 状态 |
|---|---|---|---|---|
| P2-T01 | 桥接 DTO、RPC 路由、事件总线 | P1-T03 | agent | todo |
| P2-T02 | 本地回环服务器 | P2-T01 | agent | todo |
| P2-T03 | 资源库、`IRhinoHost`、开发宿主 | P2-T02、P1-T11 | agent | todo |
| P2-T04 | 应用服务与 RPC 处理器 | P2-T03、P1-T08、P1-T09 | agent | todo |
| P2-T05 | UI 工程骨架 | P2-T03 | agent | todo |
| P2-T06 | UI：单图流程 | P2-T04、P2-T05 | agent | todo |
| P2-T07 | UI：批量流程 | P2-T06 | agent | todo |
| P2-T08 | UI：设置与服务商管理 | P2-T06 | agent | todo |
| P2-T09 | Rhino 宿主插件 | P2-T04、P0-T3、P1-T10 | agent+human | todo |
| P2-T10 | Yak 打包与发布流程 | P2-T09 | agent+human | todo |
| P2-T11 | 双平台人工验收 | P2-T07、P2-T08、P2-T10 | human | todo |
| P2-T12 | 删除旧 WPF 代码 | P2-T11 | agent | todo |

### P3 编辑功能 — 见 [`phase-3-editing.md`](phase-3-editing.md)

| ID | 任务 | 依赖 | 执行者 | 状态 |
|---|---|---|---|---|
| P3-T01 | 编辑会话模型与项目存储 | P2-T04 | agent | todo |
| P3-T02 | Rhino 文档绑定 | P3-T01、P2-T09 | agent+human | todo |
| P3-T03 | UI：历史面板与版本树 | P3-T01、P2-T06 | agent | todo |
| P3-T04 | UI：画布查看器与前后对比 | P2-T06 | agent | todo |
| P3-T05 | UI：蒙版工具 | P3-T04 | agent | todo |
| P3-T06 | 局部重绘 | P3-T05、P3-T01 | agent | todo |
| P3-T07 | Rhino 对象蒙版 | P3-T06、P2-T09 | agent+human | todo |
| P3-T08 | 多候选生成与挑选 | P3-T03 | agent | todo |
| P3-T09 | 多参考图与角色 | P3-T03 | agent | todo |
| P3-T10 | 一键操作 | P3-T06 | agent | todo |
| P3-T11 | 批量增强 | P3-T03 | agent | todo |
| P3-T12 | 输出：剪贴板、批量导出、视口背景 | P3-T03 | agent+human | todo |

### P4 云端账号 — 见 [`phase-4-cloud.md`](phase-4-cloud.md)

| ID | 任务 | 依赖 | 执行者 | 状态 |
|---|---|---|---|---|
| P4-T00 | 准备私有仓库、服务器、域名、邮件服务、上游 Key | — | human | todo |
| P4-T01 | OpenAPI 契约定稿 | P1-T03 | agent | todo |
| P4-T02 | 服务端骨架 | P4-T00、P4-T01 | agent | todo |
| P4-T03 | 登录：邮箱验证码、设备授权、令牌 | P4-T02 | agent | todo |
| P4-T04 | 点数账本与兑换码 | P4-T03 | agent | todo |
| P4-T05 | 模型目录、定价、上游通道 | P4-T02 | agent | todo |
| P4-T06 | 生成管线 | P4-T04、P4-T05、P1-T08 | agent | todo |
| P4-T07 | 管理接口与管理后台 | P4-T04、P4-T05 | agent | todo |
| P4-T08 | 插件端 `CloudBackend` 与登录客户端 | P4-T01、P1-T10 | agent | todo |
| P4-T09 | 插件 UI：登录、点数、兑换、模式切换 | P4-T08、P2-T08 | agent | todo |
| P4-T10 | 部署与运维手册 | P4-T06、P4-T07 | agent+human | todo |
| P4-T11 | 安全检查与压测 | P4-T10 | agent | todo |

## 如何派发任务

给执行 agent 的提示词模板：

```
阅读 AGENTS.md 和 docs/plan/architecture.md，然后执行 docs/plan/<阶段文件> 中的任务 <ID>。
只做这个任务范围内的事。完成后按任务的验收标准自检，把 docs/plan/README.md 中该任务的状态改为 done，
并在最终回复里列出：改动的文件、验收命令的实际输出、偏离计划的地方及原因。
```

派发前检查两点：

- 该任务「依赖」列里的任务都已是 `done`。
- 同时进行的任务不改同一批文件。同一阶段内，依赖关系互不相连的任务可以并行，例如 P1-T06 与 P1-T07、P2-T07 与 P2-T08。

计划与实际不符时（接口签名需要调整、发现计划遗漏），执行者应同步修改本目录下对应的文档，使文档始终描述实际状态。
