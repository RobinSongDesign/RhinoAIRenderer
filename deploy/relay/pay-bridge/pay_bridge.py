#!/usr/bin/env python3
"""
Payment bridge for the Chameleon relay: new-api's Epay ("易支付") protocol on one side, the
operator's own Alipay merchant account on the other.

new-api can only take Alipay and WeChat payments through an Epay-compatible gateway. Third-party
Epay platforms hold the money themselves, so this service plays that gateway for the operator's
official merchant account instead:

    wallet page --POST /submit.php--> bridge --precreate--> Alipay (当面付, QR code)
    buyer scans and pays; Alipay --notify--> bridge --verified, signed Epay notify--> new-api

The bridge credits nothing itself. It only tells new-api an order is paid after Alipay's signed
notification (or a signed query answer) says so for exactly the ordered amount; new-api does not
re-check amounts. Orders live in SQLite, so restarts lose nothing, and undelivered notifications
are retried. Providers are pluggable: WeChat Pay (Native) is a second PaymentProvider later.

Commands:
    pay_bridge.py serve               run the service (default)
    pay_bridge.py genkey [file]       create the Alipay application key pair; prints the public key

Configuration comes from environment variables, see Config.from_env.
"""
from __future__ import annotations

import base64
import hashlib
import hmac
import html
import json
import logging
import os
import re
import sqlite3
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from decimal import Decimal, InvalidOperation
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Callable, Mapping, Optional, Protocol

LOG = logging.getLogger("pay-bridge")

CHINA_TIME = timezone(timedelta(hours=8))
TRADE_SUCCESS = "TRADE_SUCCESS"
_TRADE_NO = re.compile(r"^[A-Za-z0-9_-]{1,64}$")
_MONEY = re.compile(r"^\d{1,7}(\.\d{1,2})?$")


# --------------------------------------------------------------------------------------------
# Epay protocol (exactly as github.com/Calcium-Ion/go-epay v0.0.6, which new-api uses)
# --------------------------------------------------------------------------------------------

def epay_sign_string(params: Mapping[str, str]) -> str:
    """Signing string: every non-empty parameter except sign/sign_type, sorted, joined as k=v&...

    Values are not URL-encoded, so an '&' in a value would make the string ambiguous; go-epay
    refuses those and so do we.
    """
    pairs = []
    for key in sorted(k for k, v in params.items() if k not in ("sign", "sign_type") and v != ""):
        value = params[key]
        if not key or "&" in key or "=" in key or "&" in value:
            raise ValueError("Parameter %r cannot be signed" % key)
        pairs.append("%s=%s" % (key, value))
    return "&".join(pairs)


def epay_sign(params: Mapping[str, str], key: str) -> str:
    return hashlib.md5((epay_sign_string(params) + key).encode("utf-8")).hexdigest()


def epay_verify(params: Mapping[str, str], key: str) -> bool:
    try:
        expected = epay_sign(params, key)
    except ValueError:
        return False
    return hmac.compare_digest(expected, params.get("sign", ""))


# --------------------------------------------------------------------------------------------
# Configuration
# --------------------------------------------------------------------------------------------

@dataclass(frozen=True)
class AlipayConfig:
    app_id: str
    private_key_pem: bytes
    alipay_public_key: str
    gateway: str = "https://openapi.alipay.com/gateway.do"


@dataclass(frozen=True)
class Config:
    """Settings; every field has an environment variable (see from_env)."""

    epay_pid: str
    epay_key: str
    public_url: str
    allowed_hosts: tuple
    alipay: Optional[AlipayConfig] = None
    db_path: str = "/data/orders.db"
    order_timeout: timedelta = timedelta(minutes=15)
    max_amount: Decimal = Decimal("5000")
    # Callbacks to new-api must be https; tests against local fakes turn this on.
    allow_http_callbacks: bool = False

    @classmethod
    def from_env(cls, env: Mapping[str, str]) -> "Config":
        """Reads EPAY_PID, EPAY_KEY, PUBLIC_URL, ALLOWED_HOSTS (comma separated new-api hosts),
        ALIPAY_APP_ID, ALIPAY_PRIVATE_KEY_FILE, ALIPAY_PUBLIC_KEY, ALIPAY_GATEWAY, DB_PATH,
        ORDER_TIMEOUT_MINUTES and MAX_AMOUNT."""
        def required(name: str) -> str:
            value = env.get(name, "").strip()
            if not value:
                raise ValueError("%s must be set" % name)
            return value

        alipay = None
        if env.get("ALIPAY_APP_ID", "").strip():
            key_file = env.get("ALIPAY_PRIVATE_KEY_FILE", "/data/alipay_app_private.pem")
            with open(key_file, "rb") as handle:
                private_key = handle.read()
            alipay = AlipayConfig(
                app_id=env["ALIPAY_APP_ID"].strip(),
                private_key_pem=private_key,
                alipay_public_key=required("ALIPAY_PUBLIC_KEY"),
                gateway=env.get("ALIPAY_GATEWAY", AlipayConfig.gateway).strip() or AlipayConfig.gateway,
            )
        return cls(
            epay_pid=env.get("EPAY_PID", "1001").strip(),
            epay_key=required("EPAY_KEY"),
            public_url=required("PUBLIC_URL").rstrip("/"),
            allowed_hosts=tuple(h.strip().lower() for h in required("ALLOWED_HOSTS").split(",") if h.strip()),
            alipay=alipay,
            db_path=env.get("DB_PATH", cls.db_path),
            order_timeout=timedelta(minutes=int(env.get("ORDER_TIMEOUT_MINUTES", "15"))),
            max_amount=Decimal(env.get("MAX_AMOUNT", "5000")),
        )


class BridgeError(Exception):
    """A request the bridge refuses; the message is shown to the buyer."""

    def __init__(self, status: int, message: str):
        super().__init__(message)
        self.status = status


# --------------------------------------------------------------------------------------------
# Orders
# --------------------------------------------------------------------------------------------

@dataclass
class Order:
    out_trade_no: str
    type: str
    name: str
    money: str
    notify_url: str
    return_url: str
    status: str = "pending"          # pending -> paid -> notified; or expired / mismatch
    qr_code: str = ""
    provider_trade_no: str = ""
    created_at: float = 0.0
    paid_at: float = 0.0
    last_query_at: float = 0.0
    notify_attempts: int = 0
    next_notify_at: float = 0.0


class OrderStore:
    """Orders in SQLite; one connection guarded by a lock (the server is threaded)."""

    _COLUMNS = [f for f in Order.__dataclass_fields__]

    def __init__(self, path: str):
        if path != ":memory:":
            os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
        self._db = sqlite3.connect(path, check_same_thread=False)
        self._lock = threading.Lock()
        with self._lock:
            self._db.execute(
                "CREATE TABLE IF NOT EXISTS orders (out_trade_no TEXT PRIMARY KEY, type TEXT, name TEXT, money TEXT,"
                " notify_url TEXT, return_url TEXT, status TEXT, qr_code TEXT, provider_trade_no TEXT,"
                " created_at REAL, paid_at REAL, last_query_at REAL, notify_attempts INTEGER, next_notify_at REAL)")
            self._db.commit()

    def get(self, out_trade_no: str) -> Optional[Order]:
        with self._lock:
            row = self._db.execute("SELECT %s FROM orders WHERE out_trade_no = ?" % ", ".join(self._COLUMNS), (out_trade_no,)).fetchone()
        return Order(*row) if row else None

    def save(self, order: Order) -> None:
        values = [getattr(order, c) for c in self._COLUMNS]
        with self._lock:
            self._db.execute("INSERT OR REPLACE INTO orders (%s) VALUES (%s)" % (", ".join(self._COLUMNS), ", ".join("?" * len(values))), values)
            self._db.commit()

    def with_status(self, status: str) -> list:
        with self._lock:
            rows = self._db.execute("SELECT %s FROM orders WHERE status = ? ORDER BY created_at" % ", ".join(self._COLUMNS), (status,)).fetchall()
        return [Order(*row) for row in rows]


# --------------------------------------------------------------------------------------------
# Providers
# --------------------------------------------------------------------------------------------

@dataclass(frozen=True)
class PaymentResult:
    """What a provider says about an order."""

    out_trade_no: str
    paid: bool
    amount: str = ""
    provider_trade_no: str = ""


class PaymentProvider(Protocol):
    """One payment method of the Epay protocol ("alipay", "wxpay")."""

    epay_type: str

    def create(self, order: Order, notify_url: str) -> str:
        """Creates the payment and returns the content of the QR code to show."""

    def query(self, order: Order) -> Optional[PaymentResult]:
        """Asks the provider for the order's state; None while it knows nothing yet."""

    def parse_notify(self, form: Mapping[str, str]) -> PaymentResult:
        """Verifies an asynchronous notification; raises BridgeError when it is not genuine."""


def _load_public_key(value: str):
    from cryptography.hazmat.primitives.serialization import load_pem_public_key
    text = value.strip()
    if "BEGIN" not in text:
        body = "\n".join(text[i:i + 64] for i in range(0, len(text), 64))
        text = "-----BEGIN PUBLIC KEY-----\n%s\n-----END PUBLIC KEY-----\n" % body
    return load_pem_public_key(text.encode("ascii"))


class AlipayClient:
    """Alipay OpenAPI with RSA2 (SHA256withRSA) signatures."""

    def __init__(self, config: AlipayConfig, opener: Optional[urllib.request.OpenerDirector] = None,
                 now: Callable[[], datetime] = lambda: datetime.now(CHINA_TIME)):
        from cryptography.hazmat.primitives.serialization import load_pem_private_key
        self._config = config
        self._private_key = load_pem_private_key(config.private_key_pem, password=None)
        self._public_key = _load_public_key(config.alipay_public_key)
        self._opener = opener or urllib.request.build_opener()
        self._now = now

    @property
    def app_id(self) -> str:
        return self._config.app_id

    @staticmethod
    def sign_string(params: Mapping[str, str]) -> str:
        return "&".join("%s=%s" % (k, params[k]) for k in sorted(params) if k not in ("sign", "sign_type") and params[k] != "")

    def sign(self, content: str) -> str:
        from cryptography.hazmat.primitives import hashes
        from cryptography.hazmat.primitives.asymmetric import padding
        return base64.b64encode(self._private_key.sign(content.encode("utf-8"), padding.PKCS1v15(), hashes.SHA256())).decode("ascii")

    def verify(self, content: str, signature: str) -> bool:
        from cryptography.exceptions import InvalidSignature
        from cryptography.hazmat.primitives import hashes
        from cryptography.hazmat.primitives.asymmetric import padding
        try:
            self._public_key.verify(base64.b64decode(signature), content.encode("utf-8"), padding.PKCS1v15(), hashes.SHA256())
            return True
        except (InvalidSignature, ValueError):
            return False

    def call(self, method: str, biz: Mapping[str, object], notify_url: str = "") -> dict:
        """Calls an API method and returns its verified response node."""
        params = {
            "app_id": self._config.app_id,
            "method": method,
            "format": "JSON",
            "charset": "utf-8",
            "sign_type": "RSA2",
            "timestamp": self._now().strftime("%Y-%m-%d %H:%M:%S"),
            "version": "1.0",
            "biz_content": json.dumps(biz, ensure_ascii=False, separators=(",", ":")),
        }
        if notify_url:
            params["notify_url"] = notify_url
        params["sign"] = self.sign(self.sign_string(params))
        request = urllib.request.Request(
            self._config.gateway + "?charset=utf-8", method="POST",
            data=urllib.parse.urlencode(params).encode("utf-8"),
            headers={"Content-Type": "application/x-www-form-urlencoded;charset=utf-8"})
        try:
            with self._opener.open(request, timeout=20) as response:
                text = response.read().decode("utf-8")
        except (urllib.error.URLError, OSError) as error:
            raise BridgeError(502, "支付宝暂时无法连接，请稍后再试 / Alipay is unreachable, please try again") from error
        node_name = method.replace(".", "_") + "_response"
        raw, node = self._response_node(text, node_name)
        if raw is None:
            raw, node = self._response_node(text, "error_response")
            if node is None:
                raise BridgeError(502, "支付宝返回了无法识别的结果 / Unexpected answer from Alipay")
            return node  # Gateway-level errors (bad app id, ...) are not always signed.
        signature = json.loads(text).get("sign", "")
        if not self.verify(raw, signature):
            raise BridgeError(502, "支付宝返回的签名不正确 / Alipay's answer failed verification")
        return node

    @staticmethod
    def _response_node(text: str, name: str):
        """The raw JSON text of a response node (what Alipay signs) and its parsed value."""
        key = '"%s"' % name
        start = text.find(key)
        if start < 0:
            return None, None
        colon = text.index(":", start + len(key))
        begin = colon + 1
        while text[begin] in " \t\r\n":
            begin += 1
        node, end = json.JSONDecoder().raw_decode(text, begin)
        return text[begin:end], node


class AlipayProvider:
    """Alipay 当面付 (face-to-face): precreate gives a QR code the buyer scans."""

    epay_type = "alipay"

    def __init__(self, client: AlipayClient, timeout: timedelta):
        self._client = client
        self._timeout_minutes = max(1, int(timeout.total_seconds() // 60))

    def create(self, order: Order, notify_url: str) -> str:
        node = self._client.call("alipay.trade.precreate", {
            "out_trade_no": order.out_trade_no,
            "total_amount": order.money,
            "subject": "Chameleon 账户充值",
            "timeout_express": "%dm" % self._timeout_minutes,
        }, notify_url=notify_url)
        if node.get("code") != "10000" or not node.get("qr_code"):
            LOG.warning("alipay precreate refused out_trade_no=%s code=%s sub_code=%s", order.out_trade_no, node.get("code"), node.get("sub_code"))
            raise BridgeError(502, "支付宝下单失败：%s / Alipay refused the order" % (node.get("sub_msg") or node.get("msg") or ""))
        return node["qr_code"]

    def query(self, order: Order) -> Optional[PaymentResult]:
        node = self._client.call("alipay.trade.query", {"out_trade_no": order.out_trade_no})
        if node.get("code") != "10000":
            # ACQ.TRADE_NOT_EXIST until the buyer scans the code.
            return None
        status = node.get("trade_status", "")
        return PaymentResult(
            out_trade_no=node.get("out_trade_no", order.out_trade_no),
            paid=status in ("TRADE_SUCCESS", "TRADE_FINISHED"),
            amount=node.get("total_amount", ""),
            provider_trade_no=node.get("trade_no", ""))

    def parse_notify(self, form: Mapping[str, str]) -> PaymentResult:
        if form.get("sign_type", "RSA2") != "RSA2" or not self._client.verify(AlipayClient.sign_string(form), form.get("sign", "")):
            raise BridgeError(400, "invalid signature")
        if form.get("app_id") != self._client.app_id:
            raise BridgeError(400, "notification for another application")
        return PaymentResult(
            out_trade_no=form.get("out_trade_no", ""),
            paid=form.get("trade_status") in ("TRADE_SUCCESS", "TRADE_FINISHED"),
            amount=form.get("total_amount", ""),
            provider_trade_no=form.get("trade_no", ""))


# --------------------------------------------------------------------------------------------
# new-api side
# --------------------------------------------------------------------------------------------

class NewApiNotifier:
    """Sends new-api the signed Epay notification of a paid order (GET, answers "success")."""

    def __init__(self, config: Config, opener: Optional[urllib.request.OpenerDirector] = None):
        self._config = config
        self._opener = opener or urllib.request.build_opener()

    def notify(self, order: Order) -> bool:
        params = {
            "pid": self._config.epay_pid,
            "trade_no": order.provider_trade_no or order.out_trade_no,
            "out_trade_no": order.out_trade_no,
            "type": order.type,
            "name": order.name,
            "money": order.money,
            "trade_status": TRADE_SUCCESS,
        }
        params["sign"] = epay_sign(params, self._config.epay_key)
        params["sign_type"] = "MD5"
        separator = "&" if urllib.parse.urlsplit(order.notify_url).query else "?"
        url = order.notify_url + separator + urllib.parse.urlencode(params)
        try:
            with self._opener.open(urllib.request.Request(url), timeout=15) as response:
                body = response.read(64).decode("utf-8", "replace").strip()
        except (urllib.error.URLError, OSError) as error:
            LOG.warning("new-api notify failed out_trade_no=%s error=%s", order.out_trade_no, error)
            return False
        if body != "success":
            LOG.warning("new-api refused notify out_trade_no=%s body=%r", order.out_trade_no, body[:40])
        return body == "success"


# --------------------------------------------------------------------------------------------
# Service
# --------------------------------------------------------------------------------------------

# Delays between attempts to tell new-api about a paid order.
NOTIFY_BACKOFF = (0, 15, 60, 300, 900, 3600, 3 * 3600, 6 * 3600, 12 * 3600)


class PayService:
    """The bridge's rules, independent of HTTP."""

    def __init__(self, config: Config, store: OrderStore, providers: Mapping[str, PaymentProvider],
                 notifier: NewApiNotifier, clock: Callable[[], float] = time.time):
        self._config = config
        self._store = store
        self._providers = dict(providers)
        self._notifier = notifier
        self._clock = clock
        self._lock = threading.Lock()

    def status_token(self, out_trade_no: str) -> str:
        """Lets the payment page poll its own order without exposing others."""
        return hmac.new(self._config.epay_key.encode(), b"status:" + out_trade_no.encode(), hashlib.sha256).hexdigest()[:32]

    def submit(self, form: Mapping[str, str]) -> Order:
        """Validates a signed Epay purchase and returns the order with its QR code."""
        if form.get("pid") != self._config.epay_pid or not epay_verify(form, self._config.epay_key):
            raise BridgeError(400, "签名错误 / Invalid signature")
        out_trade_no = form.get("out_trade_no", "")
        if not _TRADE_NO.match(out_trade_no):
            raise BridgeError(400, "订单号无效 / Invalid order number")
        provider = self._providers.get(form.get("type", ""))
        if provider is None:
            raise BridgeError(400, "暂不支持该支付方式 / This payment method is not available yet")
        money = self._money(form.get("money", ""))
        for name in ("notify_url", "return_url"):
            self._check_callback(form.get(name, ""))

        with self._lock:
            existing = self._store.get(out_trade_no)
            if existing:
                if existing.money != money or existing.type != provider.epay_type:
                    raise BridgeError(400, "订单信息不一致 / Order details changed")
                if existing.status != "pending" or not self._expired(existing):
                    return existing
            order = Order(out_trade_no=out_trade_no, type=provider.epay_type, name=form.get("name", "")[:64], money=money,
                          notify_url=form["notify_url"], return_url=form["return_url"], created_at=self._clock())
            order.qr_code = provider.create(order, self._config.public_url + "/notify/" + provider.epay_type)
            self._store.save(order)
        LOG.info("order created out_trade_no=%s type=%s money=%s", order.out_trade_no, order.type, order.money)
        return order

    def status(self, out_trade_no: str, token: str) -> str:
        """pending | paid | expired, as the payment page sees it; checks with the provider at most every 5 s."""
        if not hmac.compare_digest(token, self.status_token(out_trade_no)):
            raise BridgeError(403, "forbidden")
        order = self._store.get(out_trade_no)
        if order is None:
            raise BridgeError(404, "not found")
        if order.status == "pending" and self._clock() - order.last_query_at >= 5:
            self._query(order)
            order = self._store.get(out_trade_no) or order
        if order.status in ("paid", "notified"):
            return "paid"
        return "expired" if order.status != "pending" or self._expired(order) else "pending"

    def handle_notify(self, epay_type: str, form: Mapping[str, str]) -> None:
        provider = self._providers.get(epay_type)
        if provider is None:
            raise BridgeError(404, "not found")
        result = provider.parse_notify(form)
        self._apply(result)

    def tick(self) -> None:
        """Background work: deliver pending notifications, query unpaid orders, expire old ones."""
        now = self._clock()
        for order in self._store.with_status("paid"):
            if order.next_notify_at <= now:
                self._deliver(order)
        for order in self._store.with_status("pending"):
            if self._expired(order, grace=timedelta(minutes=5)):
                # One last look: a payment made at the deadline still counts.
                self._query(order)
                latest = self._store.get(order.out_trade_no)
                if latest and latest.status == "pending":
                    latest.status = "expired"
                    self._store.save(latest)
            elif now - order.last_query_at >= 15 and now - order.created_at >= 10:
                self._query(order)

    # -- internals ---------------------------------------------------------------------------

    def _query(self, order: Order) -> None:
        provider = self._providers.get(order.type)
        if provider is None:
            return
        order.last_query_at = self._clock()
        self._store.save(order)
        try:
            result = provider.query(order)
        except BridgeError as error:
            LOG.warning("query failed out_trade_no=%s error=%s", order.out_trade_no, error)
            return
        if result is not None:
            self._apply(result)

    def _apply(self, result: PaymentResult) -> None:
        if not result.paid:
            return
        with self._lock:
            order = self._store.get(result.out_trade_no)
            if order is None:
                LOG.warning("payment for unknown order out_trade_no=%s", result.out_trade_no)
                return
            if order.status in ("paid", "notified", "mismatch"):
                return
            if self._amount(result.amount) != self._amount(order.money):
                # Never credit a different amount than was ordered; needs a human to look at it.
                order.status = "mismatch"
                order.provider_trade_no = result.provider_trade_no
                self._store.save(order)
                LOG.error("amount mismatch out_trade_no=%s ordered=%s paid=%s", order.out_trade_no, order.money, result.amount)
                return
            order.status = "paid"
            order.provider_trade_no = result.provider_trade_no
            order.paid_at = self._clock()
            order.next_notify_at = self._clock()
            self._store.save(order)
        LOG.info("order paid out_trade_no=%s money=%s", order.out_trade_no, order.money)
        self._deliver(order)

    def _deliver(self, order: Order) -> None:
        if self._notifier.notify(order):
            order.status = "notified"
            LOG.info("new-api credited out_trade_no=%s", order.out_trade_no)
        else:
            order.notify_attempts += 1
            delay = NOTIFY_BACKOFF[min(order.notify_attempts, len(NOTIFY_BACKOFF) - 1)]
            order.next_notify_at = self._clock() + delay
        self._store.save(order)

    def _expired(self, order: Order, grace: timedelta = timedelta(0)) -> bool:
        return self._clock() - order.created_at > (self._config.order_timeout + grace).total_seconds()

    def _money(self, value: str) -> str:
        if not _MONEY.match(value):
            raise BridgeError(400, "金额无效 / Invalid amount")
        amount = Decimal(value)
        if amount < Decimal("0.01") or amount > self._config.max_amount:
            raise BridgeError(400, "金额超出范围 / Amount out of range")
        return "%.2f" % amount

    @staticmethod
    def _amount(value: str) -> Optional[Decimal]:
        try:
            return Decimal(value).quantize(Decimal("0.01"))
        except (InvalidOperation, ValueError):
            return None

    def _check_callback(self, url: str) -> None:
        parts = urllib.parse.urlsplit(url)
        scheme_ok = parts.scheme == "https" or (parts.scheme == "http" and self._config.allow_http_callbacks)
        if not scheme_ok or (parts.hostname or "").lower() not in self._config.allowed_hosts:
            raise BridgeError(400, "回调地址不被允许 / Callback address not allowed")


# --------------------------------------------------------------------------------------------
# HTTP
# --------------------------------------------------------------------------------------------

PAGE = """<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>扫码支付 · Pay</title><link rel="stylesheet" href="{base}/static/pay.css"></head>
<body><main class="card">
<div class="brand"></div>
<h1>支付宝扫码支付 <span>Pay with Alipay</span></h1>
<p class="amount">¥ {money}</p>
<p class="item">Chameleon 账户充值 · Account top-up</p>
<div class="qr" id="qr">{qr}</div>
<p class="hint" id="hint">请使用支付宝扫一扫完成付款<br><span>Scan with the Alipay app to pay</span></p>
<a class="open" href="{qr_url}">在手机上？点此打开支付宝 · Open Alipay</a>
<p class="meta">订单号 Order {out_trade_no}</p>
</main>
<div id="state" data-status-url="{status_url}" data-return-url="{return_url}"></div>
<script src="{base}/static/pay.js"></script></body></html>
"""

CSS = """
*{box-sizing:border-box}
body{margin:0;min-height:100vh;display:grid;place-items:center;background:#f6f6f8;color:#1d1f22;font:15px/1.6 -apple-system,BlinkMacSystemFont,"PingFang SC","Microsoft YaHei",sans-serif}
.card{background:#fff;border-radius:16px;box-shadow:0 8px 30px rgba(0,0,0,.08);padding:28px 32px;width:min(380px,92vw);text-align:center}
.brand{width:44px;height:6px;border-radius:3px;margin:0 auto 14px;background:linear-gradient(90deg,#00c8e0,#e6007e)}
h1{font-size:19px;margin:0 0 6px}h1 span,.hint span{display:block;font-size:13px;color:#6b6f76;font-weight:400}
.amount{font-size:30px;font-weight:700;margin:8px 0 0}.item{color:#6b6f76;margin:0 0 12px}
.qr{display:flex;justify-content:center}.qr svg{width:220px;height:220px;display:block}.qr.done{opacity:.15}.hint{margin:10px 0}.hint.ok{color:#1a7f37;font-weight:600}.hint.bad{color:#b42318}
.open{display:inline-block;margin:4px 0 10px;color:#1677ff;text-decoration:none;font-size:14px}
.meta{color:#9a9ea5;font-size:12px;margin:0}
"""

JS = """
(function(){var s=document.getElementById('state'),h=document.getElementById('hint'),q=document.getElementById('qr');
var url=s.getAttribute('data-status-url'),back=s.getAttribute('data-return-url'),done=false;
function tick(){if(done)return;fetch(url,{cache:'no-store'}).then(function(r){return r.json()}).then(function(d){
if(d.status==='paid'){done=true;q.className='qr done';h.className='hint ok';h.innerHTML='支付成功，正在返回…<br><span>Paid, returning…</span>';setTimeout(function(){location.href=back},1500)}
else if(d.status==='expired'){done=true;q.className='qr done';h.className='hint bad';h.innerHTML='订单已过期，请返回重新下单<br><span>This order expired; please start again</span>'}
}).catch(function(){}).then(function(){if(!done)setTimeout(tick,2500)})}
setTimeout(tick,2500)})();
"""


def render_page(config: Config, order: Order, token: str) -> str:
    import segno
    # omitsize: a viewBox instead of a fixed size, so the stylesheet can scale the code up.
    qr_svg = segno.make(order.qr_code, error="m").svg_inline(scale=1, border=2, dark="#1d1f22", omitsize=True)
    status_url = "%s/status/%s?t=%s" % (config.public_url, order.out_trade_no, token)
    return PAGE.format(
        base=html.escape(config.public_url), money=html.escape(order.money), qr=qr_svg,
        qr_url=html.escape(order.qr_code), out_trade_no=html.escape(order.out_trade_no),
        status_url=html.escape(status_url), return_url=html.escape(order.return_url))


def make_handler(service: PayService, config: Config):
    base_path = urllib.parse.urlsplit(config.public_url).path.rstrip("/")

    class Handler(BaseHTTPRequestHandler):
        server_version = "chameleon-pay-bridge"
        protocol_version = "HTTP/1.1"

        def log_message(self, fmt, *args):
            pass  # Paths carry signed parameters; requests are logged by the service instead.

        def _route(self) -> str:
            path = urllib.parse.urlsplit(self.path).path
            # Behind a reverse proxy the prefix (e.g. /pay) may or may not be stripped.
            return path[len(base_path):] if base_path and path.startswith(base_path + "/") else path

        def do_GET(self):
            route = self._route()
            query = dict(urllib.parse.parse_qsl(urllib.parse.urlsplit(self.path).query))
            try:
                if route == "/healthz":
                    self._send(200, "application/json", b'{"ok": true}')
                elif route == "/static/pay.css":
                    self._send(200, "text/css; charset=utf-8", CSS.encode())
                elif route == "/static/pay.js":
                    self._send(200, "application/javascript; charset=utf-8", JS.encode())
                elif route.startswith("/status/"):
                    status = service.status(route[len("/status/"):], query.get("t", ""))
                    self._send(200, "application/json", json.dumps({"status": status}).encode())
                elif route == "/submit.php":
                    self._submit(query)
                else:
                    raise BridgeError(404, "not found")
            except BridgeError as error:
                self._error(error)

        def do_POST(self):
            route = self._route()
            try:
                form = self._form()
                if route == "/submit.php":
                    self._submit(form)
                elif route.startswith("/notify/"):
                    service.handle_notify(route[len("/notify/"):], form)
                    self._send(200, "text/plain; charset=utf-8", b"success")
                else:
                    raise BridgeError(404, "not found")
            except BridgeError as error:
                if route.startswith("/notify/"):
                    LOG.warning("notification refused route=%s reason=%s", route, error)
                    self._send(error.status, "text/plain; charset=utf-8", b"fail")
                else:
                    self._error(error)

        def _submit(self, form: Mapping[str, str]) -> None:
            order = service.submit(form)
            if order.status in ("paid", "notified"):
                self.send_response(303)
                self.send_header("Location", order.return_url)
                self.send_header("Content-Length", "0")
                self.end_headers()
                return
            page = render_page(config, order, service.status_token(order.out_trade_no))
            self._send(200, "text/html; charset=utf-8", page.encode("utf-8"))

        def _form(self) -> dict:
            length = int(self.headers.get("Content-Length") or 0)
            if length > 64 * 1024:
                raise BridgeError(413, "too large")
            body = self.rfile.read(length).decode("utf-8", "replace") if length else ""
            return dict(urllib.parse.parse_qsl(body, keep_blank_values=True))

        def _error(self, error: BridgeError) -> None:
            page = "<!doctype html><meta charset=utf-8><title>支付 · Pay</title><p style='font:16px sans-serif;margin:40px'>%s</p>" % html.escape(str(error))
            self._send(error.status, "text/html; charset=utf-8", page.encode("utf-8"))

        def _send(self, status: int, content_type: str, body: bytes) -> None:
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("Content-Security-Policy", "default-src 'none'; style-src 'self'; script-src 'self'; connect-src 'self'; img-src 'self' data:; form-action 'self'; frame-ancestors 'none'")
            self.end_headers()
            self.wfile.write(body)

    return Handler


def build_service(config: Config, opener: Optional[urllib.request.OpenerDirector] = None,
                  clock: Callable[[], float] = time.time) -> PayService:
    providers = {}
    if config.alipay:
        providers["alipay"] = AlipayProvider(AlipayClient(config.alipay, opener), config.order_timeout)
    return PayService(config, OrderStore(config.db_path), providers, NewApiNotifier(config, opener), clock)


def run_worker(service: PayService, stop: threading.Event, interval: float = 5.0) -> None:
    while not stop.wait(interval):
        try:
            service.tick()
        except Exception:  # noqa: BLE001 - the worker must keep running.
            LOG.exception("background tick failed")


def generate_key(path: str) -> str:
    """Creates the Alipay application key pair; returns the public key to paste into Alipay."""
    from cryptography.hazmat.primitives import serialization
    from cryptography.hazmat.primitives.asymmetric import rsa
    if os.path.exists(path):
        raise SystemExit("%s already exists; delete it first if you really want a new key" % path)
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    pem = key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption())
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, "wb") as handle:
        handle.write(pem)
    public = key.public_key().public_bytes(serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo)
    return base64.b64encode(public).decode("ascii")


def main(argv: list) -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    command = argv[0] if argv else "serve"
    if command == "genkey":
        path = argv[1] if len(argv) > 1 else os.environ.get("ALIPAY_PRIVATE_KEY_FILE", "/data/alipay_app_private.pem")
        print("Application public key (paste into Alipay, 接口加签方式 → 公钥):\n" + generate_key(path))
        return
    config = Config.from_env(os.environ)
    service = build_service(config)
    stop = threading.Event()
    threading.Thread(target=run_worker, args=(service, stop), daemon=True).start()
    port = int(os.environ.get("PORT", "8090"))
    server = ThreadingHTTPServer(("0.0.0.0", port), make_handler(service, config))
    server.daemon_threads = True
    LOG.info("listening on %d, public url %s, methods %s", port, config.public_url, "alipay" if config.alipay else "none")
    server.serve_forever()


if __name__ == "__main__":
    main(sys.argv[1:])
