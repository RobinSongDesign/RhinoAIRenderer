# 中转网关（P4-R）：用 new-api 分发账号

本文是 P4 的第一阶段方案：不自建网关，先用开源的 [new-api](https://github.com/QuantumNous/new-api) 作为「账号与计费」服务端，上游接一个中转服务商（例如柏拉图 AI）。插件用户在你的网站注册、充值或兑换后，拿到一个令牌，在插件里填入即可生成。原计划的自建网关（P4-T02–T07）保留，推迟到需要浏览器登录、异步任务等能力时再做。

```
Rhino 插件 ──令牌──▶ 你的服务器（Caddy → new-api：鉴权、扣额度、兑换码、后台）
                                   │ 你在柏拉图的 Key
                                   ▼
                              柏拉图 AI（或其他上游，可配多个互为备份）
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

未验证：与真实柏拉图 AI 的连通（需要真实 Key）；`deploy/relay/Caddyfile` 未经 `caddy validate` 校验（`docker-compose.yml` 已通过 `docker compose config` 校验，镜像标签已确认存在）。

## 2. 服务器

| 项目 | 建议 |
|---|---|
| 配置 | 2 核 4G（2 核 2G 可起步） |
| 地区 | 香港：免备案，国内访问延迟低，离柏拉图的香港接口近 |
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

1. **渠道**：渠道管理 → 添加渠道。类型选 Gemini，地址填柏拉图的接口地址（插件内置的「BLT AI」用的是 `https://hk-api.gptbest.vip`，以柏拉图后台为准），Key 填你在柏拉图的 Key，模型填要开放的模型 ID（如 `gemini-3.1-flash-image-preview`）。可再加一个其他上游的渠道作备份。
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
- 只在上游授权范围内使用，不进行未经授权的转售；**请先确认柏拉图 AI 的条款允许转售**；
- 若面向中国大陆公众提供生成式 AI 服务，需履行备案、安全评估、内容安全、投诉处理、生成内容标识、日志留存、个人信息保护等义务；
- 由部署、运营和收费产生的法律责任由运营者自行承担。

起步阶段可以只用兑换码：在后台批量生成，通过发卡平台等渠道出售。正规的微信、支付宝收款通常需要营业执照。

## 5. 插件端对接

- 现在就能用：设置 → 添加服务商，接口格式选 Gemini，地址填你的域名，Key 填用户的令牌。
- P4-R2 增加内置的「中转账号」服务商（地址随版本预置，用户只填令牌）、余额显示，以及把 403 额度不足识别为「点数不足」并引导兑换。

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
