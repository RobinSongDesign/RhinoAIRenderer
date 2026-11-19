# 中转网关（P4-R）：用 new-api 分发账号

本文是 P4 的第一阶段方案：不自建网关，先用开源的 [new-api](https://github.com/QuantumNous/new-api) 作为「账号与计费」服务端，上游接一个或多个中转服务商。插件用户在你的网站注册、充值或兑换后，拿到一个令牌，在插件里填入即可生成。原计划的自建网关（P4-T02–T07）保留，推迟到需要浏览器登录、异步任务等能力时再做。

```
Rhino 插件 ──令牌──▶ 你的服务器（Caddy → new-api：鉴权、扣额度、兑换码、后台）
                                   │ 你在上游的 Key
                                   ▼
                              中转服务商 A（主）/ B（备用），自动切换
```

## 1. 实测结果

2026-11-18 在本地运行 new-api，上游指向一个模拟的图片服务（`tools/scripts/fake_image_upstream.py`），用插件真实的适配器（`tools/Chameleon.Cli`）发请求。测了三个版本：开发主干、`v0.13.2`、`v1.0.0-rc.51`。**部署请用 `v1.0.0-rc.51` 或更新的版本**。

| 检查 | v1.0.0-rc.51 | 说明 |
|---|---|---|
| Gemini 原生格式完整请求（提示词 + 源图 + 风格/材质/物体三张参考图 + 16:9 + 4K） | 通过 | 上游收到的图片数量、格式、字节数与 `imageConfig` 完全一致；返回的图片逐字节相同 |
| 16 MB 源图（请求体 21 MB） | 通过 | new-api 默认上限 `MAX_REQUEST_BODY_MB=128` |
| OpenAI 格式：带蒙版的 `/v1/images/edits`、无源图的 `/v1/images/generations` | 通过 | multipart 字段完整 |
| 按尺寸计价（同一模型 1K/2K/4K 不同价） | 通过 | 用计费表达式读取请求里的 `generationConfig.imageConfig.imageSize`，实扣 $0.067 / $0.134 / $0.24。按实际请求计价，用户无法用 1K 的价格生成 4K。`v0.13.2` 不支持此写法 |
| 上游返回 429 / 400 / 500 | 通过 | 状态码与上游消息原样传回，插件现有的错误提示可用；失败的请求不扣费 |
| 令牌错误 | 通过 | 401 `Invalid token` |
| 额度不足 | 通过 | **403**，`error.code` 为 `pre_consume_token_quota_failed`（令牌额度）或 `insufficient_user_quota`（账户余额）。插件需要把它识别为「点数不足」，而不是「Key 被拒」 |
| 查询余额 | 通过 | `GET /v1/dashboard/billing/subscription` 返回 `hard_limit_usd`（总额），`GET /v1/dashboard/billing/usage` 返回 `total_usage`（已用，单位为美分）；剩余 = `hard_limit_usd - total_usage / 100` |
| 长时间生成 | 按配置判断，未实测 | new-api 默认不限制中转时长（`RELAY_TIMEOUT=0`），等待首字节上限 30 分钟；Caddyfile 中同样设为 30 分钟 |
| 兑换码 | 未实测 | 需要管理员先在后台确认合规声明（见第 4 节），这一步应由运营者本人完成 |

未验证：与真实上游的连通（需要真实 Key）；`deploy/relay/Caddyfile` 未经 `caddy validate` 校验（`docker-compose.yml` 已通过 `docker compose config` 校验，镜像标签已确认存在）。

## 2. 服务器

| 项目 | 建议 |
|---|---|
| 配置 | 2 核 4G（2 核 2G 可起步） |
| 地区 | 东京或新加坡：免备案，国内访问延迟可接受。注意 Gemini API 的支持地区不含香港与中国大陆 |
| 带宽 | 20–30 Mbps 起；每次生成往返约 5–10 MB |
| 硬盘 | 40 GB SSD |
| 系统 | Ubuntu 22.04 / 24.04，安装 Docker 与 Docker Compose |
| 域名 | 一个子域名（如 `api.你的域名`），A 记录指向服务器 IP |

## 3. 部署步骤

文件在仓库的 `deploy/relay/`。

```bash
# 1. 把 deploy/relay 目录复制到服务器，例如 /opt/chameleon-relay
cd /opt/chameleon-relay
cp .env.example .env
# 2. 编辑 .env：DOMAIN、三个密码（openssl rand -hex 24 生成）
# 3. 启动
docker compose up -d
docker compose logs -f new-api   # 看到 "Local: http://..." 即启动成功
```

浏览器打开 `https://你的域名`，按向导创建管理员账号。之后：

1. **渠道**：渠道管理 → 添加渠道。类型选 Gemini，地址填中转服务商的接口地址，Key 填你在该服务商的 Key，模型填要开放的模型 ID（如 `gemini-nano-banana-2.1`，以上游实际提供的为准）。至少再加一个其他上游的渠道作备份（原内置的柏拉图 AI 已停止运营，2026-11 从插件中移除）。
2. **定价**：系统设置 → 分组与模型定价。图片模型改用「表达式计费」，例如：

   ```
   param("generationConfig.imageConfig.imageSize") == "4K" ? tier("4k", fixed(0.24))
     : (param("generationConfig.imageConfig.imageSize") == "2K" ? tier("2k", fixed(0.134))
     : tier("1k", fixed(0.067)))
   ```

   `fixed()` 里是每次的美元价格，按你的上游成本加利润设置。new-api 内部 1 美元 = 500000 额度单位；面向用户的展示货币和汇率可在设置中调整。
3. **登录方式**：系统设置 → 登录注册。可开邮箱验证码注册（需 SMTP）、微信登录（需公众号与 WeChat Server 组件）、OIDC 等。new-api 不原生支持手机号短信登录。
4. **署名**：AGPLv3 附加条款要求在页脚等位置保留 `Frontend design and development by New API contributors.` 与原项目链接。只部署不改代码时，没有公开源码的义务；改了代码，则需向用户提供修改后的源码。
5. **备份**：定期备份 Postgres（`docker compose exec postgres pg_dump -U newapi newapi > backup.sql`）。
6. **升级**：修改 `.env` 里的 `NEW_API_VERSION`，执行 `docker compose pull && docker compose up -d`。升级前先备份，并先在测试环境用 `fake_image_upstream.py` 复测第 1 节的项目。

## 4. 收费前必须了解

在后台开启充值、兑换码之前，new-api 要求管理员阅读并确认一份合规声明，主要内容：

- 你已合法取得所接入模型接口、账号、Key 与额度的授权；
- 只在上游授权范围内使用，不进行未经授权的转售；**请先确认上游服务商的条款允许转售**；
- 若面向中国大陆公众提供生成式 AI 服务，需履行备案、安全评估、内容安全、投诉处理、生成内容标识、日志留存、个人信息保护等义务；
- 由部署、运营和收费产生的法律责任由运营者自行承担。

起步阶段可以只用兑换码：在后台批量生成，通过发卡平台等渠道出售。正规的微信、支付宝收款通常需要营业执照。

## 5. 插件端对接（P4-R2，已完成）

- **用户自己添加**：设置 → 添加服务商，接口格式选 Gemini，地址填你的域名，模型填你开放的模型 ID；勾选「显示剩余额度（兼容 One API / new-api 的中转）」，「充值页面」填你网站的充值或兑换地址（必须 https）。保存后填入令牌。
- **随版本预置**（推荐，用户只需填令牌；当前版本已预置 `Chameleon` 服务商，指向 `https://api.robinsong.top`，域名变化时改这一项）：在 `src/Chameleon.Core/Catalog/catalog.json` 的 `providers` 中增加一项，`balanceApi` 设为 `"oneapi"`，`topUpUrl` 设为充值页面，`apiKeyUrl` 设为用户获取令牌的页面，然后发布新版本。例如：

  ```json
  {
    "id": "ChameleonRelay",
    "displayName": { "zh": "Chameleon 账号", "en": "Chameleon account" },
    "baseUrl": "https://api.你的域名",
    "apiFormat": "gemini",
    "authType": "bearer",
    "defaultModel": "gemini-3.1-flash-image-preview",
    "apiKeyUrl": "https://api.你的域名/console/token",
    "topUpUrl": "https://api.你的域名/console/topup",
    "balanceApi": "oneapi",
    "models": [ "…从 Gemini 条目中复制要开放的模型的完整定义（含 capabilities）…" ]
  }
  ```

  `models` 中每一项都要带完整的 `capabilities`（尺寸、宽高比、最多几张输入图等），直接复制 `Gemini` 条目里对应的模型即可；模型 ID 要与 new-api 渠道里开放的一致。

- 插件在服务商选择框下方显示余额（生成成功后自动刷新，也可手动刷新）和「充值」链接。
- 余额不足（new-api 返回 403 与 `insufficient_user_quota` / `pre_consume_token_quota_failed`）时，提示「点数或余额不足，请兑换或充值」，不会弹出账号模式的兑换对话框。
- 余额以美元显示（new-api 计费接口的单位）。如果你的网站对用户展示人民币，需要在网站上说明换算，或后续增加显示换算比例的设置。

## 6. 复测方法

```bash
# 模拟上游（只监听 127.0.0.1）
python3 tools/scripts/fake_image_upstream.py 7801 upstream.log result.png
# new-api 中添加 Gemini 渠道，地址 http://127.0.0.1:7801；创建令牌后：
export CHAMELEON_API_KEY=sk-你的测试令牌
dotnet tools/Chameleon.Cli/bin/Release/net8.0/chameleon-cli.dll generate \
  --base-url http://你的new-api地址 --format gemini --model gemini-3.1-flash-image-preview \
  --prompt "test" --source a.png --ref b.png:style --size 4K --aspect 16:9 --out out.png
# 对比 upstream.log 中收到的图片与参数
```
