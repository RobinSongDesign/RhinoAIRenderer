"""Tests of the payment bridge against a fake Alipay gateway and a fake new-api, on 127.0.0.1.

Run: python3 -m unittest discover -s deploy/relay/pay-bridge   (needs requirements.txt installed)
"""
import base64
import json
import os
import stat
import tempfile
import threading
import unittest
import urllib.error
import urllib.parse
import urllib.request
from datetime import timedelta
from decimal import Decimal
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa

import pay_bridge as pb

EPAY_KEY = "test-key-123"
DIRECT = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def key_pair():
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    pem = key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption())
    public = base64.b64encode(key.public_key().public_bytes(serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo)).decode()
    return key, pem, public


APP_KEY, APP_PEM, APP_PUBLIC = key_pair()
ALIPAY_KEY, _, ALIPAY_PUBLIC = key_pair()


def alipay_sign(content: str) -> str:
    return base64.b64encode(ALIPAY_KEY.sign(content.encode(), padding.PKCS1v15(), hashes.SHA256())).decode()


def serve(handler):
    server = ThreadingHTTPServer(("127.0.0.1", 0), handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server, "http://127.0.0.1:%d" % server.server_address[1]


class FakeAlipay:
    """Alipay's gateway: checks our RSA2 request signature and answers signed like Alipay."""

    def __init__(self):
        self.calls = []
        self.trade_status = None  # None: the buyer has not scanned yet
        self.paid_amount = None
        outer = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def do_POST(self):
                form = dict(urllib.parse.parse_qsl(self.rfile.read(int(self.headers["Content-Length"])).decode()))
                content = pb.AlipayClient.sign_string(form)
                public = serialization.load_der_public_key(base64.b64decode(APP_PUBLIC))
                public.verify(base64.b64decode(form["sign"]), content.encode(), padding.PKCS1v15(), hashes.SHA256())
                biz = json.loads(form["biz_content"])
                outer.calls.append((form["method"], biz, form))
                name = form["method"].replace(".", "_") + "_response"
                if form["method"] == "alipay.trade.precreate":
                    node = {"code": "10000", "msg": "Success", "out_trade_no": biz["out_trade_no"], "qr_code": "https://qr.alipay.com/test" + biz["out_trade_no"]}
                elif outer.trade_status is None:
                    node = {"code": "40004", "msg": "Business Failed", "sub_code": "ACQ.TRADE_NOT_EXIST"}
                else:
                    node = {"code": "10000", "msg": "Success", "out_trade_no": biz["out_trade_no"], "trade_no": "2026112122001",
                            "trade_status": outer.trade_status, "total_amount": outer.paid_amount}
                # Alipay signs the exact text of the response node, so keep it verbatim.
                raw = json.dumps(node, ensure_ascii=False, separators=(",", ":"))
                body = '{"%s":%s,"sign":"%s"}' % (name, raw, alipay_sign(raw))
                data = body.encode()
                self.send_response(200)
                self.send_header("Content-Type", "application/json;charset=utf-8")
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)

        self.server, self.url = serve(Handler)


class FakeNewApi:
    """new-api's /api/user/epay/notify: verifies like go-epay, answers success or fail."""

    def __init__(self):
        self.received = []
        self.answer = "success"
        outer = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def do_GET(self):
                params = dict(urllib.parse.parse_qsl(urllib.parse.urlsplit(self.path).query))
                outer.received.append(params)
                body = (outer.answer if pb.epay_verify(params, EPAY_KEY) else "fail").encode()
                self.send_response(200)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

        self.server, self.url = serve(Handler)


class EpayProtocolTests(unittest.TestCase):
    def test_matches_go_epay_signing(self):
        # Test vectors from go-epay v0.0.6 (signature_test.go).
        params = {"money": "moneyv", "device": "devicev", "empty": "", "sign": "ignored&value", "sign_type": "ignored&type"}
        self.assertEqual("3854cc9f022e0fb821bd2e002260245d", pb.epay_sign(params, "1234567"))
        callback = {"money": "1.00", "name": "商品 + 50% = x%26y", "out_trade_no": "order-1", "pid": "1",
                    "trade_no": "epay-1", "trade_status": "TRADE_SUCCESS", "type": "wxpay", "sign_type": "MD5"}
        self.assertEqual("money=1.00&name=商品 + 50% = x%26y&out_trade_no=order-1&pid=1&trade_no=epay-1&trade_status=TRADE_SUCCESS&type=wxpay",
                         pb.epay_sign_string(callback))

    def test_refuses_ambiguous_values_and_wrong_signatures(self):
        with self.assertRaises(ValueError):
            pb.epay_sign_string({"name": "a&b"})
        signed = {"money": "1.00", "pid": "1001"}
        signed["sign"] = pb.epay_sign(signed, EPAY_KEY)
        self.assertTrue(pb.epay_verify(signed, EPAY_KEY))
        self.assertFalse(pb.epay_verify(dict(signed, money="100.00"), EPAY_KEY))
        self.assertFalse(pb.epay_verify({"name": "a&b", "sign": "x"}, EPAY_KEY))


class BridgeTests(unittest.TestCase):
    def setUp(self):
        self.alipay = FakeAlipay()
        self.newapi = FakeNewApi()
        self.now = [1_800_000_000.0]
        self.config = pb.Config(
            epay_pid="1001", epay_key=EPAY_KEY, public_url="https://api.test/pay", allowed_hosts=("127.0.0.1",),
            alipay=pb.AlipayConfig(app_id="2021000000000001", private_key_pem=APP_PEM, alipay_public_key=ALIPAY_PUBLIC, gateway=self.alipay.url + "/gateway.do"),
            db_path=":memory:", allow_http_callbacks=True)
        self.service = pb.build_service(self.config, opener=DIRECT, clock=lambda: self.now[0])
        self.server, self.url = serve(pb.make_handler(self.service, self.config))

    def tearDown(self):
        for server in (self.server, self.alipay.server, self.newapi.server):
            server.shutdown()
            server.server_close()

    # -- helpers --------------------------------------------------------------------------------

    def purchase(self, **overrides):
        """What new-api's wallet page posts to /submit.php (go-epay Purchase, MD5)."""
        params = {"type": "alipay", "out_trade_no": "USR2NOabc1231800000000", "notify_url": self.newapi.url + "/api/user/epay/notify",
                  "name": "TUC10", "money": "10.00", "device": "pc", "return_url": self.newapi.url + "/usage-logs", "pid": "1001"}
        params.update(overrides)
        params["sign"] = pb.epay_sign(params, EPAY_KEY)
        params["sign_type"] = "MD5"
        return params

    def post(self, path, form, follow=True):
        request = urllib.request.Request(self.url + path, data=urllib.parse.urlencode(form).encode(), method="POST")
        opener = DIRECT if follow else urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
        try:
            with opener.open(request, timeout=10) as response:
                return response.status, response.read().decode(), response.headers
        except urllib.error.HTTPError as error:
            return error.code, error.read().decode(), error.headers

    def alipay_notify(self, out_trade_no, amount="10.00", status="TRADE_SUCCESS", app_id="2021000000000001", tamper=False):
        form = {"notify_type": "trade_status_sync", "notify_id": "n1", "app_id": app_id, "charset": "utf-8", "version": "1.0",
                "trade_no": "2026112122001", "out_trade_no": out_trade_no, "trade_status": status, "total_amount": amount,
                "subject": "Chameleon 账户充值", "gmt_payment": "2026-11-21 10:00:00"}
        form["sign"] = alipay_sign(pb.AlipayClient.sign_string(form))
        form["sign_type"] = "RSA2"
        if tamper:
            form["total_amount"] = "0.01"
        return self.post("/pay/notify/alipay", form)

    def status(self, out_trade_no):
        token = self.service.status_token(out_trade_no)
        with DIRECT.open(self.url + "/pay/status/%s?t=%s" % (out_trade_no, token), timeout=10) as response:
            return json.loads(response.read())["status"]

    # -- the payment flow -------------------------------------------------------------------------

    def test_purchase_shows_a_qr_code_for_a_signed_alipay_precreate(self):
        status, page, headers = self.post("/pay/submit.php", self.purchase())

        self.assertEqual(200, status)
        self.assertIn("¥ 10.00", page)
        self.assertIn("<svg", page)
        self.assertRegex(page, r"<svg[^>]*viewBox")  # Scalable: the page sizes it with CSS.
        self.assertIn("https://qr.alipay.com/testUSR2NOabc1231800000000", page)
        self.assertIn("default-src 'none'", headers["Content-Security-Policy"])
        method, biz, form = self.alipay.calls[0]
        self.assertEqual("alipay.trade.precreate", method)
        self.assertEqual({"out_trade_no": "USR2NOabc1231800000000", "total_amount": "10.00", "subject": "Chameleon 账户充值", "timeout_express": "15m"}, biz)
        self.assertEqual("https://api.test/pay/notify/alipay", form["notify_url"])
        self.assertEqual("RSA2", form["sign_type"])

    def test_alipay_notification_credits_the_order_once_in_new_api(self):
        order = self.purchase()
        self.post("/pay/submit.php", order)

        status, body, _ = self.alipay_notify(order["out_trade_no"])
        self.alipay_notify(order["out_trade_no"])  # Alipay repeats notifications.

        self.assertEqual((200, "success"), (status, body))
        self.assertEqual(1, len(self.newapi.received))
        sent = self.newapi.received[0]
        self.assertTrue(pb.epay_verify(sent, EPAY_KEY))
        self.assertEqual({"pid": "1001", "trade_no": "2026112122001", "out_trade_no": order["out_trade_no"], "type": "alipay",
                          "name": "TUC10", "money": "10.00", "trade_status": "TRADE_SUCCESS", "sign_type": "MD5"},
                         {k: v for k, v in sent.items() if k != "sign"})
        self.assertEqual("paid", self.status(order["out_trade_no"]))

    def test_forged_or_foreign_notifications_credit_nothing(self):
        order = self.purchase()
        self.post("/pay/submit.php", order)

        self.assertEqual((400, "fail"), self.alipay_notify(order["out_trade_no"], tamper=True)[:2])
        self.assertEqual((400, "fail"), self.alipay_notify(order["out_trade_no"], app_id="2021999999999999")[:2])
        self.assertEqual([], self.newapi.received)

    def test_a_different_paid_amount_is_never_credited(self):
        order = self.purchase()
        self.post("/pay/submit.php", order)

        self.alipay_notify(order["out_trade_no"], amount="0.01")

        self.assertEqual([], self.newapi.received)
        self.assertEqual("mismatch", self.service._store.get(order["out_trade_no"]).status)

    def test_without_a_notification_the_payment_is_found_by_querying(self):
        order = self.purchase()
        self.post("/pay/submit.php", order)
        self.assertEqual("pending", self.status(order["out_trade_no"]))

        self.alipay.trade_status, self.alipay.paid_amount = "TRADE_SUCCESS", "10.00"
        self.now[0] += 20
        self.service.tick()

        self.assertEqual(1, len(self.newapi.received))
        self.assertEqual("paid", self.status(order["out_trade_no"]))

    def test_new_api_failures_are_retried_with_backoff(self):
        order = self.purchase()
        self.post("/pay/submit.php", order)
        self.newapi.answer = "fail"
        self.alipay_notify(order["out_trade_no"])
        self.assertEqual(1, len(self.newapi.received))

        self.service.tick()  # Too early for the next attempt.
        self.assertEqual(1, len(self.newapi.received))

        self.newapi.answer = "success"
        self.now[0] += 16
        self.service.tick()
        self.assertEqual(2, len(self.newapi.received))
        self.assertEqual("notified", self.service._store.get(order["out_trade_no"]).status)

    def test_unpaid_orders_expire(self):
        order = self.purchase()
        self.post("/pay/submit.php", order)

        self.now[0] += 21 * 60
        self.service.tick()

        self.assertEqual("expired", self.status(order["out_trade_no"]))
        self.assertEqual([], self.newapi.received)

    def test_resubmitting_reuses_the_code_and_a_paid_order_returns_to_the_site(self):
        order = self.purchase()
        self.post("/pay/submit.php", order)
        self.post("/pay/submit.php", order)
        self.assertEqual(1, sum(1 for c in self.alipay.calls if c[0] == "alipay.trade.precreate"))

        self.alipay_notify(order["out_trade_no"])
        status, _, headers = self.post("/pay/submit.php", order, follow=False)
        self.assertEqual(303, status)
        self.assertEqual(order["return_url"], headers["Location"])

    # -- refusals ---------------------------------------------------------------------------------

    def test_unsigned_foreign_or_unsupported_purchases_are_refused(self):
        bad_sign = self.purchase()
        bad_sign["money"] = "0.01"
        cases = {
            "signature": bad_sign,
            "pid": self.purchase(pid="9999"),
            "callback host": self.purchase(notify_url="https://evil.example.com/notify"),
            "method": self.purchase(type="wxpay"),
            "amount": self.purchase(money="99999.00"),
            "order number": self.purchase(out_trade_no="bad no!"),
        }
        for name, form in cases.items():
            with self.subTest(name):
                self.assertEqual(400, self.post("/pay/submit.php", form)[0])
        self.assertEqual([], self.alipay.calls)

    def test_status_needs_the_order_token(self):
        order = self.purchase()
        self.post("/pay/submit.php", order)
        with self.assertRaises(urllib.error.HTTPError) as caught:
            DIRECT.open(self.url + "/pay/status/%s?t=wrong" % order["out_trade_no"], timeout=10)
        self.assertEqual(403, caught.exception.code)

    def test_health_and_static_files(self):
        with DIRECT.open(self.url + "/pay/healthz", timeout=10) as response:
            self.assertEqual({"ok": True}, json.loads(response.read()))
        with DIRECT.open(self.url + "/pay/static/pay.js", timeout=10) as response:
            self.assertIn("application/javascript", response.headers["Content-Type"])


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


class KeyTests(unittest.TestCase):
    def test_genkey_writes_a_private_file_and_prints_a_usable_public_key(self):
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "keys", "alipay_app_private.pem")
            public = pb.generate_key(path)

            self.assertEqual(0o600, stat.S_IMODE(os.stat(path).st_mode))
            serialization.load_der_public_key(base64.b64decode(public))
            with self.assertRaises(SystemExit):
                pb.generate_key(path)  # Never overwrites an existing key.

    def test_config_from_environment(self):
        with tempfile.NamedTemporaryFile("wb", delete=False) as handle:
            handle.write(APP_PEM)
        try:
            config = pb.Config.from_env({
                "EPAY_KEY": EPAY_KEY, "PUBLIC_URL": "https://api.rhchameleon.top/pay/", "ALLOWED_HOSTS": "api.rhchameleon.top, API.robinsong.top",
                "ALIPAY_APP_ID": "2021004162613734", "ALIPAY_PRIVATE_KEY_FILE": handle.name, "ALIPAY_PUBLIC_KEY": ALIPAY_PUBLIC,
                "ORDER_TIMEOUT_MINUTES": "10", "MAX_AMOUNT": "2000"})
        finally:
            os.unlink(handle.name)
        self.assertEqual("https://api.rhchameleon.top/pay", config.public_url)
        self.assertEqual(("api.rhchameleon.top", "api.robinsong.top"), config.allowed_hosts)
        self.assertEqual(timedelta(minutes=10), config.order_timeout)
        self.assertEqual(Decimal("2000"), config.max_amount)
        self.assertEqual("https://openapi.alipay.com/gateway.do", config.alipay.gateway)
        with self.assertRaises(ValueError):
            pb.Config.from_env({"PUBLIC_URL": "x", "ALLOWED_HOSTS": "x"})


if __name__ == "__main__":
    unittest.main()
