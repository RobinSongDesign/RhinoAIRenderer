# P4 云端账号与分发

目标：用户在插件里登录账号即可使用，不需要自己申请 API Key；我们通过兑换码分发点数，在后台管理用户、定价和上游通道。

规格见 `cloud-api.md`。插件端任务（P4-T08、P4-T09）在本仓库完成；服务端任务（P4-T02–T07、T10、T11）在独立的私有仓库完成。

依赖关系：服务端任务只依赖 P1（复用 `Chameleon.Core` 的适配器），可以与 P2、P3 并行。插件 UI 任务 P4-T09 依赖 P2。

---

## P4-T00 准备工作

**执行者**：human

| 事项 | 说明 |
|---|---|
| 私有仓库 | 新建私有仓库（建议名 `chameleon-server`），把本仓库添加为 submodule，路径 `external/chameleon` |
| 服务器 | 一台 2 核 4 GB 起步的云主机，位置按 D3 选择（默认香港或新加坡），需能访问上游服务商，同时国内访问延迟可接受 |
| 域名与证书 | 一个域名，例如 `api.<你的域名>`；证书由 Caddy 自动申请 |
| 邮件服务 | 一个可用的 SMTP 账号用于发送登录验证码；确认发往国内常见邮箱的到达率 |
| 上游 Key | 至少一个上游通道的 Key（Gemini 原生或中转站），确认其条款允许此类用途 |
| 定价 | 每个模型、每档尺寸消耗多少点；一个点对应多少钱（仅用于你自己核算） |
| 合规 | 确认所在地区对收款、对外提供此类服务的要求。这一项不是开发任务，但决定能否公开分发 |

完成后把非机密信息（域名、服务器位置、模型与定价表）写入私有仓库的 `docs/deployment-notes.md`。机密只放在服务器环境变量或密钥管理里。

---

## P4-T01 OpenAPI 契约定稿

**依赖**：P1-T03　**仓库**：本仓库

### 目标

把 `cloud-api.md` 第 3–6 节写成 `contracts/cloud-api.openapi.yaml`（OpenAPI 3.1），作为插件与服务端共同遵守的契约。

### 实现要点

- 覆盖 `/v1` 下的全部接口；管理接口不放进这份公开契约，在私有仓库另写。
- 每个接口写明请求体、响应体、错误响应与示例。错误码枚举与 `bridge-protocol.md` 第 5 节保持一致。
- `GenerationRequest` 的 JSON 形式与 `Chameleon.Core.Generation.GenerationRequest` 字段一一对应（图片字段改为 multipart 部分名的引用）。
- CI 增加契约校验步骤（用一个 OpenAPI lint 工具），格式错误则失败。
- 在 `cloud-api.md` 顶部注明：契约文件生效后，两者不一致时以契约文件为准。

### 验收标准

lint 通过；`cloud-api.md` 中每个 `/v1` 接口都能在契约文件里找到。

---

## P4-T02 服务端骨架

**依赖**：P4-T00、P4-T01　**仓库**：私有仓库

### 目标

可运行、可测试、可部署的空服务。

### 结构

```
chameleon-server/
  external/chameleon/                 # submodule（本仓库）
  src/Chameleon.Server/               # ASP.NET Core 8 Minimal API
    Program.cs
    Auth/  Accounts/  Catalog/  Generations/  Admin/  Infrastructure/
  src/Chameleon.Server.Data/          # EF Core 实体、DbContext、迁移
  web/                                # 登录页、设备确认页、管理后台（React + Vite，沿用插件前端的技术栈）
  tests/Chameleon.Server.Tests/       # xUnit + Testcontainers（PostgreSQL）
  deploy/                             # docker-compose.yml、Caddyfile、备份脚本
  AGENTS.md                           # 从本仓库复制并按服务端调整
  docs/
```

### 实现要点

- `Chameleon.Server` 引用 `external/chameleon/src/Chameleon.Core/Chameleon.Core.csproj`。
- EF Core + Npgsql；按 `cloud-api.md` 第 8 节建全部表与索引的初始迁移；启动时自动应用迁移（可用开关关闭）。
- 配置全部来自环境变量（`cloud-api.md` 第 9 节），启动时校验必填项，缺失则拒绝启动并指明缺哪一项。
- 统一错误处理中间件：异常 → `{ error: { code, message, detail } }`；`RenderException` 复用 Core 的错误码映射。
- 请求日志：方法、路径、状态码、耗时、用户 id、请求 id；不记录请求体、`Authorization` 头。
- `GET /healthz`（进程存活）与 `GET /readyz`（数据库可连）。
- `docker-compose.yml`：`server`、`postgres`、`caddy` 三个服务，数据卷分别用于数据库、结果图片、密钥环。
- CI：构建、测试（用 Testcontainers 起 PostgreSQL）、构建镜像。
- 测试基建：`WebApplicationFactory` + 每个测试类独立的数据库；可注入的时钟与假邮件发送器。

### 验收标准

`docker compose up` 后 `/healthz` 与 `/readyz` 返回 200；`dotnet test` 通过；空数据库启动后所有表存在。

---

## P4-T03 登录：邮箱验证码、设备授权、令牌

**依赖**：P4-T02

### 目标

实现 `cloud-api.md` 第 3 节。

### 实现要点

- 邮箱统一转小写并去除首尾空白；验证码 6 位数字，数据库存哈希；比较用常数时间。
- 限速：同一邮箱 60 秒内只发一次、每小时最多 5 次；同一 IP 每小时最多 20 次请求验证码；验证失败 5 次作废。
- `IEmailSender`：SMTP 实现与测试用的内存实现。邮件模板中英文两版，按 `Accept-Language` 选择。
- 网页会话用 HttpOnly、Secure、SameSite=Lax 的 Cookie；表单带 CSRF 令牌。
- 设备授权：`userCode` 8 位，字符集去掉 `0O1IL`，展示为 `XXXX-XXXX`；`deviceCode` 只存哈希；轮询间隔小于 `interval` 时返回 `slow_down`。
- 令牌：访问令牌 JWT（HS256，15 分钟）；刷新令牌随机 32 字节，存哈希，每次刷新轮换；检测到已轮换的旧令牌被再次使用时吊销整个会话。
- `ADMIN_EMAILS` 中的邮箱登录时赋予 `admin` 角色。
- 被停用的账号：登录、刷新、调用接口一律 401。
- 网页（`web/`）：登录页、设备确认页（显示客户端名称、平台、请求时间，按钮「授权」「拒绝」）、结果页（提示可以回到 Rhino）。中英文。

### 测试

完整流程（请求设备码 → 网页登录 → 确认 → 轮询拿到令牌 → 调用 `/v1/me`）；验证码过期、错误、超次数；限速；设备码过期与拒绝；刷新轮换与重放检测；停用账号。

---

## P4-T04 点数账本与兑换码

**依赖**：P4-T03

### 目标

实现 `cloud-api.md` 第 4 节以及兑换码的生成（管理接口部分在 P4-T07 暴露，这里先实现领域逻辑）。

### 实现要点

- `LedgerService`：唯一允许修改 `users.credits` 的地方。每个操作在一个事务内：`SELECT ... FOR UPDATE` 锁用户行 → 校验 → 更新余额 → 插入流水（带 `balance_after`）。
- 操作：`Redeem(userId, code)`、`Reserve(userId, amount, generationId)`、`Refund(generationId)`、`AdminAdjust(userId, delta, reason, adminId)`。
- `Refund` 幂等：用 `generations.refunded` 标志与唯一约束保证同一生成只退一次。
- 余额不得为负，数据库加 `CHECK (credits >= 0)`。
- 兑换码：16 个字符（去掉易混淆字符的大写字母与数字），分四组显示；存 SHA-256；兑换时在事务内把 `redeemed_by` 从空更新为用户 id，更新行数为 0 表示已被使用。
- 兑换接口对输入做规范化（去空格与连字符、转大写）。
- 幂等键：`Idempotency-Key` 相同且请求体相同返回首次结果；请求体不同返回 409。

### 测试

- 不变量：随机执行 1,000 次混合操作后，每个用户 `credits` 等于流水之和，且从未为负。
- 并发：同一用户 50 个并发预扣，成功数乘以单价不超过初始余额；同一兑换码 20 个并发兑换，只有一个成功。
- 兑换码的各种失败情形与限速。

---

## P4-T05 模型目录、定价、上游通道

**依赖**：P4-T02

### 目标

实现 `GET /v1/catalog`，以及模型、价格、通道的数据层与选择逻辑。

### 实现要点

- 初始数据：首次启动时把 `Chameleon.Core` 内置 `catalog.json` 的模型、风格模板、默认系统提示词、一键操作导入数据库；价格初始为空，未定价的模型不出现在 `/v1/catalog`。
- `/v1/catalog` 的响应带 `ETag`（目录版本号），支持 304。
- `ChannelSelector`：给定模型，从启用且支持该模型的通道中按权重随机选择；近 60 秒内失败过的通道降权；全部不可用时抛 `ModelUnavailable`。
- 通道的 Key 用 Data Protection 加解密；任何接口响应与日志都不包含 Key。
- 由通道生成 `ProviderProfile` 并经 `BackendFactory` 得到 `DirectBackend`，Key 通过一个只读的内存 `ISecretStore` 传入。
- 价格查询：`PriceService.GetPrice(modelId, imageSize)`，未定价抛 `InvalidRequest`。

### 测试

目录的导入与输出；ETag；通道选择的权重分布与失败降权；Key 加解密往返；未定价模型不可见。

---

## P4-T06 生成管线

**依赖**：P4-T04、P4-T05、P1-T08

### 目标

实现 `cloud-api.md` 第 6 节：提交、排队、执行、结算或退回、取结果、取消。

### 实现要点

- 提交：解析 multipart → 校验（模型已上架、尺寸已定价、图片可识别、数量不超过能力）→ 检查用户并发上限 → `LedgerService.Reserve` → 把输入图片写到临时目录 → 插入 `generations`（`queued`）→ 入队 → 返回 202。任何一步失败都不能留下已扣点而无记录的状态：预扣与插入记录在同一事务。
- 执行：`BackgroundService` 从进程内 `Channel` 取任务，全局并发上限可配置；选择通道 → 构造 `GenerationRequest`（必要时追加服务端系统提示词）→ `DirectBackend.GenerateAsync` → 结果图片写入 `STORAGE_PATH/{id}/{index}.{ext}` → 状态置为 `succeeded` 并标记已结算。
- 失败：记录错误码与已脱敏的详情 → `LedgerService.Refund` → 状态置为 `failed`。
- 取消：`queued` 直接取消并退回；`running` 触发取消令牌，若上游已返回则按成功处理。
- 无论成败，结束后删除输入图片临时文件。
- 启动恢复：把遗留的 `queued` 任务重新入队（输入文件仍在时），否则按失败退回；遗留的 `running` 一律按失败退回。
- 清理任务：每小时删除超过保留期的结果图片与对应记录的图片行。
- 长轮询：`?wait=` 用每个生成一个的完成信号实现，不轮询数据库。
- 取图接口校验归属，设置 `Cache-Control: private`。
- 限速：每个用户每分钟最多提交 30 次。

### 测试

用假的 `IRenderBackend`（可控延迟与失败）替换真实后端：成功路径的余额与流水；各类失败都退回且只退一次；取消两种状态；并发上限；幂等键；余额不足；进程重启恢复（重建宿主后检查状态与余额）；他人不可取图；保留期清理。

---

## P4-T07 管理接口与管理后台

**依赖**：P4-T04、P4-T05

### 目标

实现 `cloud-api.md` 第 7 节与对应的网页。

### 实现要点

- 管理接口全部校验 `admin` 角色，写入 `audit_log`（谁、何时、做了什么、对象、参数摘要；不含机密）。
- 兑换码批次：生成后可在 24 小时内下载 CSV（码、点数、到期时间）；到时清空明文列。批次页面显示已兑换数量与比例。
- 管理后台页面（`web/admin/`）：
  - 概览：今日与近 30 天的注册数、生成数、成功率、点数消耗，按模型的分布。
  - 用户：搜索、详情（余额、流水、最近生成）、调整点数（必须填原因）、停用与启用。
  - 兑换码：批次列表、新建批次、导出、作废。
  - 模型与定价：上架开关、显示名、能力、各档价格。
  - 通道：新增与编辑（Key 只能写入，不回显）、启用开关、最近错误。
  - 目录：风格模板、默认系统提示词、一键操作的编辑。
  - 生成日志：按用户、状态、时间筛选；显示错误码与详情，不显示图片。
- 管理后台用浏览器会话登录（同一套邮箱验证码），仅 `admin` 可进入。

### 测试

每个管理接口的权限检查（普通用户 403）；审计日志写入；批次导出在 24 小时后不可用；端到端（Playwright）：登录后台 → 新建批次 → 导出 → 调整某用户点数。

---

## P4-T08 插件端 `CloudBackend` 与登录客户端

**依赖**：P4-T01、P1-T10　**仓库**：本仓库

### 目标

在 `Chameleon.Core` 中实现账号模式的后端，与 `DirectBackend` 实现同一个接口。

### 涉及文件

- `src/Chameleon.Core/Cloud/CloudApiClient.cs`：契约中各接口的类型化封装。
- `src/Chameleon.Core/Cloud/AuthSession.cs`：令牌的保存、刷新、登出。
- `src/Chameleon.Core/Cloud/DeviceLoginFlow.cs`：设备授权流程。
- `src/Chameleon.Core/Backends/CloudBackend.cs`：`IRenderBackend` 的实现。
- `src/Chameleon.Core/Cloud/CloudCatalogCache.cs`。

### 实现要点

- 服务地址：编译期默认值可被设置中的 `cloud.baseUrl` 覆盖（便于连测试环境）。只接受 `https:`，例外是 `http://127.0.0.1` 与 `http://localhost`（本地开发）。
  - 实现说明：`CloudEndpoint.DefaultBaseUrl` 目前为 `null`（网关尚未部署）。没有地址时不提供账号模式；P4-T10 部署后填入正式地址。
- `AuthSession`：刷新令牌存 `ISecretStore`（名字 `cloud:refreshToken`），访问令牌只在内存；过期前 60 秒或收到 401 时刷新；并发请求共享同一次刷新；刷新失败（令牌失效）则转为未登录并触发 `Changed` 事件。
- `DeviceLoginFlow.StartAsync` 返回验证地址与用户码；后台按 `interval` 轮询，处理 `authorization_pending`、`slow_down`（间隔加 5 秒）、`expired_token`、拒绝；可取消。
- `CloudBackend`：
  - `Id` 为 `cloud`；`ListModelsAsync` 与 `GetCapabilities` 来自目录缓存。
  - `GenerateAsync`：multipart 提交（带随机 `Idempotency-Key`，重试时复用同一个）→ 以 `?wait=20` 长轮询直到终态 → 下载图片 → 返回 `GenerationResult`（`Usage.CreditsCharged` 取自响应）。
  - 取消令牌触发时调用 `DELETE /v1/generations/{id}`。
  - 服务端错误码一一映射回 `RenderErrorCode`。
  - 未登录时抛 `AuthFailed`。
- `CloudCatalogCache`：带 `If-None-Match` 拉取，缓存到磁盘，启动时先用缓存再后台刷新。
- `AccountClient`：`GetMe`、`Redeem`、`GetLedger`、`Estimate`（由目录价格本地计算，不请求网络）。

### 测试

在 `tests/Chameleon.Core.Tests` 中用一个按契约实现的内存假服务器（自定义 `HttpMessageHandler`）：登录全流程与各种失败；并发请求只触发一次刷新；刷新失败转未登录；生成的成功、失败、点数不足、取消；幂等键复用；目录缓存与 304。另提供一个可选的集成测试类，设置环境变量 `CHAMELEON_CLOUD_TEST_URL` 时对真实测试环境运行。

---

## P4-T09 插件 UI：登录、点数、兑换、模式切换

**依赖**：P4-T08、P2-T08　**仓库**：本仓库

### 目标

让用户在界面上完成登录、查看点数、兑换、在两种接入方式间切换。

### 实现要点

- 宿主侧：实现 `bridge-protocol.md` 4.7 节的方法与 `account.changed` 事件；`BackendService` 把 `cloud` 加入后端列表，`ready` 取决于是否登录；`AppBootstrapper` 注册 `CloudBackend`。开发宿主提供一个内存假云端，支持登录（自动在 2 秒后确认）、兑换码 `TEST-TEST-TEST-TEST`、扣点。
- 首次启动（既没有任何 Key、也未登录）显示选择页：「登录账号使用」与「使用自己的 API Key」。之后可在设置中切换。
- 登录对话框：显示用户码与「已在浏览器中打开验证页面」的说明、重新打开链接的按钮、取消按钮；登录成功后自动关闭。
- 右上角账号区：未登录时显示「登录」；已登录显示邮箱与点数，点击展开：兑换、流水、退出。
- 兑换对话框：输入框自动分组格式化；成功后显示增加的点数与新余额；失败按错误码提示。
- 账号模式下，生成按钮旁显示本次消耗的点数（多候选时为总数）；点数不足时禁用并提示兑换。
- `insufficient_credits` 错误直接弹出兑换对话框。
- 账号模式下隐藏服务商与 Key 相关设置；风格模板、一键操作使用服务端下发的目录。
- 任务完成后刷新点数。
- 全部新增文字提供中英文。

### 测试

端到端（开发宿主的假云端）：首次启动选择登录 → 登录完成 → 显示点数 → 兑换 → 生成后点数减少 → 点数不足时的提示 → 退出登录后后端变为未就绪 → 切换到自带 Key 模式。

---

## P4-T10 部署与运维手册

**依赖**：P4-T06、P4-T07　**执行者**：agent 编写，人执行部署

### 产出

- `deploy/docker-compose.yml`、`deploy/Caddyfile`（自动证书、请求体上限、仅转发到 `server`）。
- `deploy/backup.sh`：每日 `pg_dump` 到本地并保留 14 天，可选同步到对象存储。
- `docs/runbook.md`：首次部署步骤、环境变量清单、升级步骤（拉镜像、迁移、回滚方法）、备份恢复演练步骤、常见故障（上游全部失败、邮件发不出、磁盘满）的处理、如何新增管理员、如何轮换 `JWT_SIGNING_KEY`（会使所有会话失效）。
- 监控：`/readyz` 外部拨测；磁盘空间告警；日志中的错误率。先用最简单的方案（定时脚本 + 邮件通知），写明后续可接入的方向。

### 人工验收

在正式服务器上完成部署；用测试账号走通登录、兑换、生成；演练一次备份恢复到另一台机器。

---

## P4-T11 安全检查与压测

**依赖**：P4-T10

### 检查清单

- 所有接口的鉴权与归属校验（越权读取他人的生成、流水、图片）。
- 管理接口的角色校验。
- 输入校验：超大请求体、非图片文件、超长字段、畸形 multipart。
- 限速是否生效：登录验证码、兑换、提交生成。
- 日志与错误响应中不出现：访问令牌、刷新令牌、验证码、上游 Key、兑换码明文。
- 响应头：HSTS、`X-Content-Type-Options`、管理后台的 CSP。
- 依赖库的已知漏洞扫描。
- 账本不变量在压测后仍成立。

### 压测

用假上游（固定延迟 20 秒）模拟 200 个用户各提交 5 次生成：无 5xx；无余额不一致；内存与磁盘占用在预期内。把结果与容量估算写入 `docs/capacity.md`。

### 产出

`docs/security-review.md`：逐项结论与修复记录。

---

## 本阶段完成的标志

一个新用户在全新安装的插件中：选择登录 → 浏览器里用邮箱完成登录 → 输入你发放的兑换码 → 生成图片 → 点数正确扣减。你在管理后台能看到这个用户、这次兑换和这次生成。
