# 中转网关（P4-R）：用 new-api 分发账号

本文是 P4 的第一阶段方案：不自建网关，先用开源的 [new-api](https://github.com/QuantumNous/new-api) 作为「账号与计费」服务端，上游接一个或多个中转服务商。插件用户在你的网站注册、充值或兑换后，在插件里用网站的账号密码登录即可生成（插件自动在账号下创建令牌），也可以手动复制令牌填入。原计划的自建网关（P4-T02–T07）保留，推迟到需要浏览器登录、异步任务等能力时再做。

```
Rhino 插件 ──账号登录 / 令牌──▶ 你的服务器（Caddy → new-api：鉴权、扣额度、兑换码、后台）
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

1. **渠道**：渠道管理 → 添加渠道。类型选 Gemini，地址填中转服务商的接口地址，Key 填你在该服务商的 Key，模型填要开放的模型 ID（如 `gemini-nano-banana-2.1`，以上游实际提供的为准）。至少再加一个其他上游的渠道作备份（原内置的柏拉图 AI 已停止运营，2026-11 从插件中移除）。上游是 APIMart 时，渠道地址填桥接服务，见第 7 节。
2. **定价**：系统设置 → 分组与模型定价。图片模型改用「表达式计费」，例如：

   ```
   param("generationConfig.imageConfig.imageSize") == "4K" ? tier("4k", fixed(0.24))
     : (param("generationConfig.imageConfig.imageSize") == "2K" ? tier("2k", fixed(0.134))
     : tier("1k", fixed(0.067)))
   ```

   `fixed()` 里是每次的美元价格，按你的上游成本加利润设置。new-api 内部 1 美元 = 500000 额度单位；面向用户的展示货币和汇率可在设置中调整。
3. **登录方式**：系统设置 → 登录注册。可开邮箱验证码注册（需 SMTP）、微信登录（需公众号与 WeChat Server 组件）、OIDC 等。new-api 不原生支持手机号短信登录。插件内登录只支持「用户名/邮箱 + 密码」（可带两步验证），因此要保证：
   - **密码登录保持开启**；用微信等方式注册的用户，需要在网站个人设置里设一个密码，才能在插件里登录（否则只能复制令牌）。
   - **不要给登录开启 Turnstile 人机验证**，插件无法完成验证。
   - **不要开启「登录密码加密传输」**（`PASSWORD_LOGIN_ENCRYPTION_ENABLED`，默认关闭）。插件与网站之间已经是 HTTPS。
   - **关闭「额度查询接口返回令牌额度而非用户额度」**（运营设置，`DisplayTokenStatEnabled`，默认开启）。插件登录后创建的令牌不限额度、直接消耗账号余额；不关闭时插件只能显示「不限额」，关闭后显示的是账号余额。
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

- **账号登录**（推荐）：内置的 `Chameleon` 服务商带 `"signInApi": "newapi"`。用户在主面板或设置里点「登录账号」，输入网站的用户名（或邮箱）和密码，开了两步验证的再输入验证码。插件随即在该账号下查找或创建名为 `Chameleon for Rhino (机器名)` 的令牌（不限额度、永不过期），把它当作 Key 保存在系统钥匙串中；密码不保存。登录框里有「注册账号」「忘记密码」链接，分别打开网站的 `/sign-up` 与 `/forgot-password`。「退出」只删除本机保存的令牌，网站上的令牌保留，下次登录复用；要彻底作废，到网站「令牌管理」删除。插件内不支持微信扫码、Passkey 登录。

  new-api 限制同一 IP 每 20 分钟最多 20 次登录（含网页登录；环境变量 `CRITICAL_RATE_LIMIT` / `CRITICAL_RATE_LIMIT_DURATION`）。超过后返回空内容的 429，插件提示「登录尝试次数过多」。没有 Redis 时，计数存在内存里，`docker compose restart new-api` 可立即清除。

  在设置里对这类中转服务商点「测试」，插件只查一次余额（免费），不发生成请求：中转按次计价，测试生成也会扣用户的钱。2026-11-19 用本地运行的 new-api `v1.0.0-rc.51` 实测通过：错误密码被拒、普通登录、开启两步验证后的登录（错误验证码被拒）、再次登录复用同一个令牌（不重复创建）、关闭 `DisplayTokenStatEnabled` 前显示「不限额」与关闭后显示账号余额。尚未在正式站点 `api.robinsong.top` 上验证。注意：new-api 的登录错误信息固定为英文，插件在其上方显示本地化的「登录失败」提示。

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

## 7. 上游：APIMart（2026-11）

[APIMart](https://apimart.ai) 提供 Nano Banana 系列模型，有按张计价的「Ext」版本，价格远低于官方。它本身也是 new-api 搭建的。

### 7.1 为什么需要桥接服务

APIMart 的图片模型**只能**用它自己的异步任务接口：`POST /v1/images/generations` 提交，`GET /v1/tasks/{id}` 轮询，结果是 24–72 小时有效的图片链接。它的 Google 原生格式（`/v1beta/models/...:generateContent`）只开放了文字模型。2026-11-20 实测，用图片模型调用时返回 500「ratio or price is not configured」或「does not support pricing_mode image_modalities」，失败请求不扣费。

插件和 new-api 的 Gemini 渠道都只会同步调用，因此在服务器上增加了一个桥接服务 `deploy/relay/apimart-bridge/apimart_bridge.py`：

```
插件 ──Gemini 同步请求──▶ new-api（账号、按尺寸计费）──Gemini 渠道──▶ apimart-bridge ──提交、轮询、下载──▶ APIMart
```

桥接服务的工作：
- 把请求里的文字合并为 `prompt`。
- 图片按原顺序转成 `image_urls`（base64 Data URL）。
- `aspectRatio` 转成 `size`，`imageSize` 转成 `resolution`。
- 每 3 秒查询一次任务，完成后下载图片，以 Gemini 的格式（`inlineData`）原样返回。

插件和 new-api 都不用改，按尺寸计价的表达式照常生效。

其他特点：
- 只用 Python 标准库，在 `python:3.12-alpine` 官方镜像里直接运行，不需要构建，占用约 30 MB 内存。
- 不对外开放端口。
- APIMart 的 Key 就是 new-api 渠道的 Key，由 new-api 每次请求带过来，桥接服务不保存、不记录。
- 提示词也不记录。

模型对应关系（插件中的模型 ID → APIMart 模型），可用环境变量 `BRIDGE_MODEL_MAP` 覆盖：

| 插件模型 | APIMart 模型 | 支持尺寸 | APIMart 成本（每张，2026-11-20） | Google 官方价 |
|---|---|---|---|---|
| `gemini-nano-banana-2.1` | `gemini-nano-banana-2.1-ext` | 1K / 2K / 4K | $0.012 / $0.014 / $0.018 | $0.0336 / $0.0504 / $0.1134 |
| `gemini-3.1-flash-image` | `gemini-3.1-flash-image-preview`（nano-banana-2-ext） | 0.5K–4K | 0.5K $0.015，1K–4K 同上 | $0.045–$0.151 |
| `gemini-3-pro-image` | `gemini-3-pro-image-preview`（nano-banana-pro-ext） | 1K / 2K / 4K | 1K–2K $0.02，4K $0.03 | $0.134 / $0.24 |
| `gemini-3.1-flash-lite-image` | `gemini-3.1-flash-lite-image-ext` | 仅 1K | $0.008 | $0.0336 |

请求的尺寸不受支持时，向上取最近的一档；超过上限时用最大一档。

只要文字、不要图片的请求（`responseModalities` 中没有 `IMAGE`，例如密钥测试）直接返回 400，不转给 APIMart。否则一句 "Hi" 也会变成一次按张计费的出图任务。

### 7.2 实测（2026-11-20）

- 直连 APIMart 异步接口，1K、一张源图：30 秒完成，扣费 $0.012，与价格表一致。
- 插件 CLI → 本地桥接服务 → 真实 APIMart，1K、16:9，带源图和风格参考图：64 秒，返回 1376×768 的 JPEG，画面保留了源图的构图。
- 插件 CLI → 本地 new-api `v1.0.0-rc.51` → 桥接服务 → 模拟的 APIMart：2K 与 4K 各一次。按表达式分别扣 $0.04 和 $0.06，插件读到的余额同步减少。请求中两张图片的顺序、16:9 与尺寸都正确传到 APIMart。
- 桥接服务有 18 个单元测试（`python3 -m unittest discover -s deploy/relay/apimart-bridge`），CI 中运行。

未验证：在正式服务器上运行；2K、4K 与 Pro 模型的真实生成；Ext 版本的长期稳定性。

### 7.3 部署（在已有的 `/opt/chameleon-relay` 上增加）

服务器上的 `docker-compose.yml` 不动，在同一目录新建 `docker-compose.override.yml`。只用默认文件名时，`docker compose` 会自动合并两个文件。内容与 `deploy/relay/docker-compose.nginx.yml` 中的 `apimart-bridge` 一段相同（`services:` 下只有这一个服务）。目录属于 root，命令要加 `sudo`。2026-11-21 在东京服务器上按此部署成功。

```bash
cd /opt/chameleon-relay
sudo mkdir -p apimart-bridge
BASE=https://raw.githubusercontent.com/RobinSongDesign/RhinoAIRenderer/claude/rhinoairender-dev-plan-lkoak6/deploy/relay
sudo curl -fsSL -o apimart-bridge/apimart_bridge.py $BASE/apimart-bridge/apimart_bridge.py
sudo nano docker-compose.override.yml        # 写入 services: 与 apimart-bridge 一段
sudo docker compose config -q && sudo docker compose up -d
sudo docker compose ps                       # apimart-bridge 显示 healthy
sudo docker compose exec new-api wget -q -O - http://apimart-bridge:8080/healthz   # {"ok": true}
```

更新桥接服务：重新执行上面的 `curl` 下载 `apimart_bridge.py`，然后 `sudo docker compose restart apimart-bridge`。

new-api 后台：
1. **渠道**：类型 Gemini，代理地址 `http://apimart-bridge:8080`，密钥填 APIMart 的 Key。模型填 `gemini-nano-banana-2.1,gemini-3.1-flash-image,gemini-3-pro-image,gemini-3.1-flash-lite-image`（只开放你要卖的）。
2. **定价**：每个模型都用「表达式计费」，按尺寸设价，例如 Nano Banana 2.1：

   ```
   param("generationConfig.imageConfig.imageSize") == "4K" ? tier("4k", fixed(0.06))
     : (param("generationConfig.imageConfig.imageSize") == "2K" ? tier("2k", fixed(0.04))
     : tier("1k", fixed(0.03)))
   ```

   必须用表达式按次计价：桥接服务报告的 token 数是固定的名义值，按 token 计价会算错。

### 7.4 设置项（`.env`）

| 变量 | 默认 | 说明 |
|---|---|---|
| `BRIDGE_OFFICIAL_FALLBACK` | `false` | Ext 任务失败时改走 APIMart 官方通道。成功率更高，但按 token 计费，单价是 Ext 的数倍，而 new-api 只按表达式收用户的钱 |
| `BRIDGE_ERROR_LANGUAGE` | `zh` | APIMart 任务失败原因的语言，会显示给插件用户 |
| `BRIDGE_TASK_TIMEOUT_SECONDS` | `240` | 等待任务的上限。要小于插件每次请求的 5 分钟上限，否则插件已放弃、new-api 仍会扣费 |
| `BRIDGE_MODEL_MAP` | — | JSON，覆盖或增加模型对应关系，例如 `{"gemini-3-pro-image": {"upstream": "gemini-3-pro-image-preview-official", "resolutions": ["1K","2K","4K"], "supportsFallback": false}}` |

### 7.5 错误与费用

| 情况 | 桥接服务返回 | new-api 向用户扣费 | APIMart 向你扣费 |
|---|---|---|---|
| 成功 | 200 | 是 | 是 |
| 任务失败，原因在请求（内容安全、图片无效） | 400，附 APIMart 的原因 | 否 | 否（全额退款） |
| 任务失败，原因在上游 | 502（new-api 会尝试其他渠道） | 否 | 否 |
| 你的 APIMart 余额不足 | 503「上游账户余额不足」 | 否 | 否 |
| 超过 240 秒未完成 | 504 | 否 | **可能是**：任务之后仍可能完成并扣费 |

### 7.6 风险

- 「Ext」是 APIMart 的非官方通道，价格低但来源与稳定性无法核实。保留官方版或其他上游作为备用渠道。
- 尚未阅读 APIMart 的服务条款，不确定是否允许转售。收费前请先确认。
- 每张图片要等 30–65 秒（实测），比直连 Google 慢。

