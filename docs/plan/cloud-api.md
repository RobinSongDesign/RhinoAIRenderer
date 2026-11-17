# 云端网关规格

> 机器可读的契约见 [`contracts/cloud-api.openapi.yaml`](../../contracts/cloud-api.openapi.yaml)（P4-T01）。两者不一致时以契约文件为准。

账号模式下，插件通过这套接口使用生成能力；上游服务商的 Key 只存在于服务端。
本文件是设计规格，P4-T01 据此写出机器可读的 `contracts/cloud-api.openapi.yaml`，之后以 OpenAPI 文件为准。

## 1. 组成

```
插件（CloudBackend）──HTTPS──▶ Caddy（TLS）──▶ Chameleon.Server（ASP.NET Core 8）
                                                  │  ├─ PostgreSQL 16
浏览器（登录页、设备确认页、管理后台）──────────▶│  ├─ 结果图片存储（本地卷，后续可换 S3 兼容存储）
                                                  │  └─ 邮件发送（SMTP）
                                                  └──▶ 上游服务商（经 Chameleon.Core 的适配器）
```

服务端通过 git submodule 引入本仓库，直接引用 `src/Chameleon.Core/Chameleon.Core.csproj`，复用 `GeminiAdapter`、`OpenAIImagesAdapter`、`PromptComposer` 和错误模型。

## 2. 通用约定

- 基础路径 `/v1`；管理接口 `/admin/v1`。
- 鉴权：`Authorization: Bearer {accessToken}`。访问令牌是 JWT（HS256，15 分钟有效），载荷含 `sub`（用户 id）、`role`（`user` / `admin`）、`sid`（会话 id）。
- 错误响应统一为 `{ "error": { "code": "...", "message": "...", "detail": "..." } }`，`code` 的取值与 `bridge-protocol.md` 第 5 节相同，HTTP 状态码对应：400 `invalid_request`、401 `auth_failed`、402 `insufficient_credits`、404 `not_found`、409 `conflict`、429 `rate_limited`、502 `provider_error`、504 `timeout`。
- 列表接口用游标分页：`?cursor=&limit=`，返回 `{ items, nextCursor }`。
- 写接口支持 `Idempotency-Key` 请求头（提交生成、兑换码），24 小时内相同键返回首次结果。
- 请求体上限 40 MB。
- 时间为 ISO 8601 UTC。点数为整数。

## 3. 登录

采用设备授权流程：插件不接触密码，用户在浏览器里完成登录并确认设备。

```
插件                         服务端                          浏览器
 │ POST /v1/auth/device        │                               │
 │────────────────────────────▶│                               │
 │ deviceCode, userCode,       │                               │
 │ verificationUrl, interval   │                               │
 │◀────────────────────────────│                               │
 │ 打开 verificationUrl ───────────────────────────────────────▶│
 │                             │◀── 邮箱 + 验证码登录 ─────────│
 │                             │◀── 确认「授权此设备」 ────────│
 │ POST /v1/auth/token（轮询） │                               │
 │────────────────────────────▶│                               │
 │ accessToken, refreshToken   │                               │
 │◀────────────────────────────│                               │
```

| 接口 | 说明 |
|---|---|
| `POST /v1/auth/device` | 请求体 `{ clientName, clientVersion, platform }`。返回 `{ deviceCode, userCode, verificationUrl, verificationUrlComplete, expiresIn: 600, interval: 3 }`。`userCode` 为 8 位易读字符（去掉易混淆字符），`deviceCode` 为 32 字节随机值。 |
| `POST /v1/auth/token` | 请求体 `{ deviceCode }`。未确认返回 400 `authorization_pending`；过快返回 400 `slow_down`；过期返回 400 `expired_token`；成功返回 `{ accessToken, expiresIn, refreshToken, user }`，并使 `deviceCode` 失效。 |
| `POST /v1/auth/refresh` | 请求体 `{ refreshToken }`。返回新的 `accessToken` 与新的 `refreshToken`（轮换）。旧刷新令牌被再次使用时，吊销整个会话。 |
| `POST /v1/auth/logout` | 吊销当前会话。 |
| `GET /login`、`POST /login/code`、`POST /login/verify` | 网页：输入邮箱 → 收 6 位验证码（10 分钟有效，最多尝试 5 次，同一邮箱 60 秒内只发一次）→ 建立浏览器会话（HttpOnly Cookie）。首次登录即注册。 |
| `GET /device?code={userCode}`、`POST /device/approve` | 网页：已登录用户确认或拒绝设备。 |

刷新令牌为不透明随机串，60 天有效，数据库只存其 SHA-256。

## 4. 账号与点数

| 接口 | 说明 |
|---|---|
| `GET /v1/me` | `{ id, email, credits, createdAt }` |
| `POST /v1/redeem` | 请求体 `{ code }`。成功返回 `{ creditsAdded, credits }`。兑换码不存在、已使用、已过期、已作废分别返回 404、409、410、410。同一用户每分钟最多尝试 10 次。 |
| `GET /v1/ledger` | 账本流水，按时间倒序。 |

### 4.1 计费规则

- 价格表按「模型 × 图片尺寸」定价，单位为点数。
- **提交时预扣**：在一个数据库事务里锁定用户行，检查余额不低于价格，扣减余额并写入一条 `reserve` 流水。余额不足返回 402。
- **成功时结算**：把该生成记录标记为已结算，不再变动余额。
- **失败、取消、被拦截时退回**：加回余额并写入一条 `refund` 流水，引用同一个生成 id。每个生成最多退回一次。
- 账本只追加，不修改、不删除。任意时刻 `users.credits` 必须等于该用户全部流水 `delta` 之和，P4-T04 有不变量测试。
- 服务重启时，处于 `running` 状态超过超时时间的生成一律按失败处理并退回。

## 5. 目录

| 接口 | 说明 |
|---|---|
| `GET /v1/catalog` | 返回 `{ models: [{ id, displayName, capabilities, prices: { "1K": 4, "2K": 8, ... } }], styleTemplates, defaultSystemPrompt, quickActions, limits: { maxConcurrency, maxUploadBytes }, version }`。结构与内置 `catalog.json` 一致，多出 `prices` 与 `limits`。支持 `If-None-Match`。 |

风格模板与系统提示词由服务端下发，更新不需要发布新版插件。

## 6. 生成

长任务采用「提交 → 轮询 → 取结果」，不依赖长连接。

| 接口 | 说明 |
|---|---|
| `POST /v1/generations` | `multipart/form-data`：`request` 部分为 JSON（字段同 `GenerationRequest`，不含图片字节，图片以部分名引用），`source`、`mask`、`ref0`…`refN` 部分为图片。校验后预扣点数并入队，返回 202 `{ id, status: "queued", creditsReserved }`。 |
| `GET /v1/generations/{id}` | `{ id, status, createdAt, finishedAt?, images?: [{ index, url, mimeType, width, height }], text?, creditsCharged, error? }`。`status` 取 `queued`、`running`、`succeeded`、`failed`、`cancelled`。支持 `?wait=20` 长轮询（最长 25 秒）。 |
| `GET /v1/generations/{id}/images/{index}` | 返回图片字节，需鉴权，只有创建者可取。 |
| `DELETE /v1/generations/{id}` | 取消。排队中的直接取消并退回；运行中的尽力取消，结果以最终状态为准。 |
| `GET /v1/generations` | 当前用户最近的生成记录。 |

约束：

- 每个用户同时处于 `queued` 或 `running` 的生成数量不超过 `limits.maxConcurrency`（默认 3），超出返回 429。
- 上游调用的超时为 5 分钟；可重试错误最多自动重试 1 次，仅在同一通道失败时切换到同模型的下一个可用通道。
- 结果图片保存 7 天后清理。源图、蒙版、参考图在生成结束后立即删除，不留存。
- 服务端在转发前可以追加或替换系统提示词（按目录配置）。

## 7. 管理接口

全部要求 `role = admin`，并写入审计日志。

| 接口 | 说明 |
|---|---|
| `GET /admin/v1/users?query=` | 用户列表与搜索 |
| `GET /admin/v1/users/{id}` | 用户详情、余额、最近流水与生成 |
| `POST /admin/v1/users/{id}/credits` | 请求体 `{ delta, reason }`，写入 `admin_adjust` 流水 |
| `POST /admin/v1/users/{id}/disable`、`/enable` | 停用、启用账号；停用时吊销全部会话 |
| `POST /admin/v1/redeem-batches` | 请求体 `{ name, count, credits, expiresAt?, note? }`，生成一批兑换码 |
| `GET /admin/v1/redeem-batches`、`GET /admin/v1/redeem-batches/{id}/codes.csv` | 批次列表、导出明文兑换码（仅在生成后 24 小时内可导出，之后只存哈希） |
| `POST /admin/v1/redeem-batches/{id}/revoke` | 作废批次内未使用的码 |
| `GET/POST/PUT/DELETE /admin/v1/models` | 模型、显示名、能力、价格表、是否上架 |
| `GET/POST/PUT/DELETE /admin/v1/channels` | 上游通道：名称、Base URL、接口格式、鉴权方式、Key、支持的模型、权重、启用状态 |
| `GET/PUT /admin/v1/catalog` | 风格模板、默认系统提示词、一键操作 |
| `GET /admin/v1/generations?userId=&status=` | 生成日志（不含图片内容） |
| `GET /admin/v1/stats?from=&to=` | 按天统计：注册数、生成数、成功率、点数消耗、按模型分布 |

第一个管理员通过环境变量 `ADMIN_EMAILS` 指定：这些邮箱登录后自动获得 `admin` 角色。

## 8. 数据库表

| 表 | 主要字段 |
|---|---|
| `users` | `id` (uuid)、`email` (唯一，小写)、`role`、`credits` (int，不为负)、`disabled`、`created_at`、`last_login_at` |
| `login_codes` | `email`、`code_hash`、`expires_at`、`attempts`、`consumed_at` |
| `sessions` | `id`、`user_id`、`refresh_hash`、`client_name`、`platform`、`created_at`、`last_used_at`、`expires_at`、`revoked_at` |
| `device_grants` | `device_code_hash`、`user_code` (唯一)、`status` (`pending` / `approved` / `denied` / `consumed`)、`user_id?`、`client_name`、`platform`、`expires_at`、`last_polled_at` |
| `ledger` | `id` (自增)、`user_id`、`delta`、`balance_after`、`type` (`redeem` / `reserve` / `refund` / `admin_adjust`)、`ref_type`、`ref_id`、`note`、`created_at` |
| `redeem_batches` | `id`、`name`、`credits`、`count`、`expires_at`、`created_by`、`created_at`、`revoked_at` |
| `redeem_codes` | `id`、`batch_id`、`code_hash` (唯一)、`code_plain?` (24 小时后清空)、`redeemed_by?`、`redeemed_at?` |
| `models` | `id`、`display_name`、`capabilities` (jsonb)、`prices` (jsonb)、`enabled`、`sort` |
| `channels` | `id`、`name`、`base_url`、`api_format`、`auth_type`、`api_key_enc`、`models` (text[])、`weight`、`enabled`、`last_error_at` |
| `generations` | `id` (uuid)、`user_id`、`model_id`、`image_size`、`status`、`credits_reserved`、`settled`、`refunded`、`channel_id?`、`error_code?`、`error_detail?`、`prompt_chars`、`created_at`、`started_at?`、`finished_at?`、`idempotency_key?` |
| `generation_images` | `generation_id`、`index`、`path`、`mime_type`、`width`、`height`、`bytes` |
| `catalog_entries` | `key` (`style_templates` / `default_system_prompt` / `quick_actions`)、`value` (jsonb)、`version`、`updated_at` |
| `audit_log` | `id`、`admin_id`、`action`、`target`、`detail` (jsonb)、`created_at` |

- 兑换码格式 `XXXX-XXXX-XXXX-XXXX`（去掉易混淆字符的大写字母与数字），数据库存 SHA-256。
- 上游 Key 用 ASP.NET Core Data Protection 加密后存入 `api_key_enc`，密钥环持久化到挂载卷。
- 不保存用户提示词全文，只记录长度（`prompt_chars`）；排查问题时可通过配置临时开启全文记录。

## 9. 配置项

服务端全部通过环境变量配置：

| 变量 | 说明 |
|---|---|
| `DATABASE_URL` | PostgreSQL 连接串 |
| `JWT_SIGNING_KEY` | 至少 32 字节的随机值 |
| `PUBLIC_BASE_URL` | 对外地址，用于生成 `verificationUrl` |
| `SMTP_HOST`、`SMTP_PORT`、`SMTP_USER`、`SMTP_PASSWORD`、`MAIL_FROM` | 发送验证码邮件 |
| `ADMIN_EMAILS` | 逗号分隔的管理员邮箱 |
| `STORAGE_PATH` | 结果图片目录 |
| `DATA_PROTECTION_PATH` | 密钥环目录 |
| `RESULT_RETENTION_DAYS` | 默认 7 |
| `MAX_CONCURRENCY_PER_USER` | 默认 3 |
