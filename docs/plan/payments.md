# 收款：支付宝（微信后续）

new-api 只能通过「易支付（Epay）」协议收支付宝和微信付款。第三方易支付平台会先代收你的钱再结算给你，有被冻结、卷款的风险，也可能违规，所以不用。我们在服务器上运行一个自己的转接服务 `deploy/relay/pay-bridge/`：对 new-api 扮演易支付网关，对支付宝用运营者自己的商户号（当面付），钱直接进运营者的支付宝账户。

```
钱包页 ──POST /pay/submit.php──▶ pay-bridge ──alipay.trade.precreate──▶ 支付宝（生成二维码）
买家扫码付款；支付宝 ──异步通知──▶ pay-bridge ──验签、核对金额后，签名的易支付通知──▶ new-api 入账
```

## 1. 规则

- 只有支付宝签名的异步通知或查单结果显示「已支付」，且**实付金额与订单金额完全一致**时，才通知 new-api 入账。new-api 收到通知后不再核对金额，所以这一步必须由转接服务把关。金额不一致的订单标为 `mismatch`，不入账，需要人工处理。
- 对 new-api 的下单请求校验商户号、MD5 签名、订单号格式、金额范围，以及回调地址的域名（`ALLOWED_HOSTS`）。
- 签名规则与 new-api 使用的 `github.com/Calcium-Ion/go-epay` v0.0.6 一致，已用该库自带的测试向量核对。
- 支付宝通知送不到时（网络或配置问题）有兜底：付款页每 5 秒查一次单，后台每 15 秒查一次未付订单。订单到期（默认 15 分钟，另加 5 分钟宽限）前会再查一次，确认未付才关闭。
- 通知 new-api 失败时按 15 秒、1 分钟、5 分钟……最长 12 小时的间隔重试。订单存放在 SQLite（`pay-data/orders.db`），服务重启不会丢单。
- 支付宝的应用私钥只在服务器上生成和保存（`pay-data/alipay_app_private.pem`，权限 600），不经过聊天或仓库。
- 退款不经过转接服务：在支付宝商家后台退款，再到 new-api 后台扣减该用户的额度。

## 2. 测试（2026-11-21）

- 单元与集成测试共 15 项：`pip install -r deploy/relay/pay-bridge/requirements.txt && python3 -m unittest discover -s deploy/relay/pay-bridge`，已加入 CI。测试用本地模拟的支付宝网关（校验我们的 RSA2 请求签名，用独立的「支付宝」密钥对回应签名）和模拟的 new-api。
- 与真实 new-api `v1.0.0-rc.51` 联调：
  1. 管理员配置易支付并确认合规声明。
  2. 用户在钱包下单 10 美元额度。
  3. new-api 返回的表单提交到转接服务，生成付款页。
  4. 用模拟支付宝签名的通知标记为已付。
  5. new-api 为该用户入账 10 美元，充值记录状态为成功。
- **未验证**：真实的支付宝接口。需要当面付审核通过后，在服务器上用真实的小额付款验证。

## 3. 支付宝侧准备

1. 开通「当面付」（个体工商户即可，审核约 1～2 个工作日）。
2. 开放平台 → 应用（例如已上线的「应用2.0签约…」，AppID `2021004162613734`）→「可调用产品」中确认有当面付。
3. 若该应用正被其他系统使用，不要更换它的密钥，另建一个应用。

## 4. 部署（东京服务器）

服务器上 new-api 由 `/opt/chameleon-relay/docker-compose.yml` 加 `docker-compose.override.yml` 运行，Nginx 的 `api.rhchameleon.top` 站点在 `/etc/nginx/sites-available/api-rhchameleon.conf`。

```bash
cd /opt/chameleon-relay
BASE=https://raw.githubusercontent.com/RobinSongDesign/RhinoAIRenderer/claude/rhinoairender-dev-plan-lkoak6/deploy/relay
sudo mkdir -p pay-bridge pay-data
for f in Dockerfile requirements.txt pay_bridge.py pay.env.example; do sudo curl -fsSL -o pay-bridge/$f $BASE/pay-bridge/$f; done
sudo cp pay-bridge/pay.env.example pay.env && sudo chmod 600 pay.env
sudo sed -i "s/^EPAY_KEY=$/EPAY_KEY=$(openssl rand -hex 24)/; s/^ALIPAY_APP_ID=$/ALIPAY_APP_ID=2021004162613734/" pay.env
```

在 `docker-compose.override.yml` 的 `services:` 下追加 `pay-bridge` 一段（与 `deploy/relay/docker-compose.nginx.yml` 中的相同），然后：

```bash
sudo docker compose build pay-bridge
# 生成支付宝应用密钥：私钥写入 pay-data，屏幕上打印应用公钥
sudo docker compose run --rm pay-bridge python /app/pay_bridge.py genkey
```

把打印出的应用公钥填到支付宝开放平台：应用 →「开发设置」→ 接口加签方式 → 公钥。保存后页面会显示「支付宝公钥」，把它写进 `pay.env` 的 `ALIPAY_PUBLIC_KEY=` 一行。然后：

```bash
sudo curl -fsSL -o /etc/nginx/snippets/chameleon-pay.conf $BASE/nginx-pay.conf
# 在 HTTPS 的 server 块（含 listen 443）中加入：include snippets/chameleon-pay.conf;
sudo nano /etc/nginx/sites-available/api-rhchameleon.conf
sudo nginx -t && sudo systemctl reload nginx
sudo docker compose up -d pay-bridge
curl -s https://api.rhchameleon.top/pay/healthz      # {"ok": true}
```

## 5. new-api 设置

系统设置 → 计费 → 支付网关：

| 项目 | 值 |
|---|---|
| 支付地址 | `https://api.rhchameleon.top/pay` |
| 易支付商户 ID | `1001`（与 `pay.env` 的 `EPAY_PID` 相同） |
| 易支付商户密钥 | `pay.env` 的 `EPAY_KEY`（`sudo grep EPAY_KEY /opt/chameleon-relay/pay.env`） |
| 充值方式 | 只保留「支付宝」，微信开通前先删除 |
| 充值价格 | 每 1 美元额度收多少人民币（默认 7.3） |
| 最低充值 | 按需 |

「服务器地址」必须是 `https://api.rhchameleon.top`：回调地址由它生成，并且要在 `ALLOWED_HOSTS` 中。

## 6. 上线验收

1. 把「充值价格」临时改为 `0.01`，用自己的账号在钱包充值 1 美元（实付 0.01 元），扫码付款。
2. 付款页显示「支付成功」并跳回网站，钱包余额增加，充值记录为成功。
3. 在支付宝商家后台看到这笔收款后，把「充值价格」改回正式价格。
4. 出问题时查看日志：`sudo docker compose logs --tail 50 pay-bridge`。

## 7. 微信支付（后续）

开通条件：已认证的服务号或小程序、微信支付商户号、Native 支付权限。开通后新增一个 `PaymentProvider`（Native 下单 `code_url`，APIv3 签名，通知用 AES-256-GCM 解密），并在 new-api 充值方式中加回「微信」。
