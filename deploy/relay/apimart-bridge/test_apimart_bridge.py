"""Tests of the Gemini-to-APIMart bridge, against a fake APIMart on 127.0.0.1. No network access.

Run: python3 -m unittest discover -s deploy/relay/apimart-bridge
"""
import base64
import json
import threading
import unittest
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import apimart_bridge as bridge

KEY = "test-key-123"
PNG = base64.b64decode(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")
# No proxy: the sandbox and CI may set HTTPS_PROXY, and the fake runs on loopback.
DIRECT = urllib.request.build_opener(urllib.request.ProxyHandler({}))


class FakeApimart:
    """Scriptable APIMart: records submissions, answers task queries from a list of states."""

    def __init__(self):
        self.submissions = []
        self.auth = []
        self.states = ["processing", "completed"]
        self.submit_status = 200
        self.failure = None
        outer = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def do_POST(self):
                outer.auth.append(self.headers.get("Authorization"))
                outer.submissions.append(json.loads(self.rfile.read(int(self.headers["Content-Length"]))))
                if outer.submit_status != 200:
                    self._json(outer.submit_status, {"error": {"code": outer.submit_status, "message": "scripted failure"}})
                else:
                    self._json(200, {"code": 200, "data": [{"status": "submitted", "task_id": "task_1"}]})

            def do_GET(self):
                outer.auth.append(self.headers.get("Authorization"))
                if self.path.startswith("/files/"):
                    self.send_response(200)
                    self.send_header("Content-Type", "application/octet-stream")
                    self.send_header("Content-Length", str(len(PNG)))
                    self.end_headers()
                    self.wfile.write(PNG)
                    return
                state = outer.states.pop(0) if len(outer.states) > 1 else outer.states[0]
                data = {"id": "task_1", "status": state}
                if state == "completed":
                    data["result"] = {"images": [{"url": [outer.base + "/files/out.png"], "expires_at": 0}]}
                if state == "failed":
                    data["error"] = outer.failure
                self._json(200, {"code": 200, "data": data})

            def _json(self, status, payload):
                body = json.dumps(payload).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.base = "http://127.0.0.1:%d" % self.server.server_address[1]
        threading.Thread(target=self.server.serve_forever, daemon=True).start()

    def close(self):
        self.server.shutdown()
        self.server.server_close()


def gemini_body(size="2K", aspect="16:9", images=2):
    parts = [{"text": "Render the capture photorealistically."}]
    for i in range(images):
        parts.append({"inlineData": {"mimeType": "image/png" if i == 0 else "image/jpeg", "data": "AAA" + str(i)}})
    config = {"imageSize": size}
    if aspect:
        config["aspectRatio"] = aspect
    return {"contents": [{"parts": parts}], "tools": [{"google_search": {}}],
            "generationConfig": {"responseModalities": ["TEXT", "IMAGE"], "imageConfig": config}}


class ModelRouteTests(unittest.TestCase):
    def test_rounds_up_to_a_supported_tier_and_caps_at_the_largest(self):
        route = bridge.ModelRoute("m", ("1K", "2K", "4K"))
        self.assertEqual("1K", route.resolution_for("0.5K"))
        self.assertEqual("1K", route.resolution_for(None))
        self.assertEqual("2K", route.resolution_for("2k"))
        self.assertEqual("1K", bridge.ModelRoute("lite", ("1K",)).resolution_for("4K"))
        self.assertEqual("0.5K", bridge.ModelRoute("flash", bridge.RESOLUTIONS).resolution_for("512"))

    def test_model_map_from_the_environment_extends_the_defaults(self):
        config = bridge.BridgeConfig.from_env({
            "BRIDGE_MODEL_MAP": json.dumps({"gemini-3-pro-image": {"upstream": "gemini-3-pro-image-preview-official", "resolutions": ["1K", "2K", "4K"], "supportsFallback": False}}),
            "BRIDGE_OFFICIAL_FALLBACK": "true",
            "APIMART_BASE_URL": "https://example.test/",
        })
        self.assertEqual("gemini-3-pro-image-preview-official", config.route_for("gemini-3-pro-image").upstream)
        self.assertEqual("gemini-nano-banana-2.1-ext", config.route_for("gemini-nano-banana-2.1").upstream)
        self.assertTrue(config.official_fallback)
        self.assertEqual("https://example.test", config.upstream_base)
        with self.assertRaises(ValueError):
            bridge.BridgeConfig.from_env({"BRIDGE_MODEL_MAP": json.dumps({"x": {"upstream": "y", "resolutions": ["3K"]}})})


class TranslatorTests(unittest.TestCase):
    def setUp(self):
        self.translator = bridge.GeminiRequestTranslator(bridge.BridgeConfig(official_fallback=True))

    def test_maps_model_prompt_images_in_order_ratio_and_size(self):
        task = self.translator.translate("gemini-nano-banana-2.1", gemini_body())
        self.assertEqual("gemini-nano-banana-2.1-ext", task.model)
        self.assertEqual("Render the capture photorealistically.", task.prompt)
        self.assertEqual(("data:image/png;base64,AAA0", "data:image/jpeg;base64,AAA1"), task.image_urls)
        self.assertEqual("16:9", task.size)
        self.assertEqual("2K", task.resolution)
        self.assertEqual({"model": "gemini-nano-banana-2.1-ext", "prompt": task.prompt, "resolution": "2K", "n": 1,
                          "size": "16:9", "image_urls": list(task.image_urls), "official_fallback": True}, task.to_json())

    def test_without_a_ratio_the_size_is_left_to_the_first_image(self):
        task = self.translator.translate("gemini-3-pro-image", gemini_body(aspect=None, images=1))
        self.assertNotIn("size", task.to_json())

    def test_system_instruction_and_snake_case_fields_are_read(self):
        body = {"system_instruction": {"parts": [{"text": "You are a renderer."}]},
                "contents": [{"parts": [{"text": "Make it dusk."}, {"inline_data": {"mime_type": "image/webp", "data": "BBB"}}]}],
                "generation_config": {"image_config": {"image_size": "4K"}}}
        task = self.translator.translate("gemini-3.1-flash-lite-image", body)
        self.assertEqual("You are a renderer.\n\nMake it dusk.", task.prompt)
        self.assertEqual(("data:image/webp;base64,BBB",), task.image_urls)
        self.assertEqual("1K", task.resolution)
        # The lite model has no official channel to fall back to.
        self.assertFalse(task.official_fallback)

    def test_unknown_models_pass_through(self):
        task = self.translator.translate("some-new-image-model", gemini_body(size="4K"))
        self.assertEqual("some-new-image-model", task.model)
        self.assertEqual("4K", task.resolution)
        self.assertFalse(task.official_fallback)

    def test_text_only_requests_are_refused_before_reaching_apimart(self):
        body = {"contents": [{"parts": [{"text": "Hi"}]}], "generationConfig": {"responseModalities": ["TEXT"]}}
        with self.assertRaises(bridge.BridgeError) as caught:
            self.translator.translate("gemini-nano-banana-2.1", body)
        self.assertEqual(400, caught.exception.status)
        # Requests that do not say which modalities they want are image requests.
        self.assertEqual("1K", self.translator.translate("gemini-nano-banana-2.1", {"contents": [{"parts": [{"text": "Hi"}]}]}).resolution)

    def test_a_request_without_text_is_rejected(self):
        with self.assertRaises(bridge.BridgeError) as caught:
            self.translator.translate("gemini-nano-banana-2.1", {"contents": [{"parts": [{"inlineData": {"data": "x"}}]}]})
        self.assertEqual(400, caught.exception.status)


class EndToEndTests(unittest.TestCase):
    """The bridge's HTTP server in front of the fake APIMart, called like new-api calls it."""

    def setUp(self):
        self.fake = FakeApimart()
        self.clock = [0.0]
        self.sleeps = []
        self.config = bridge.BridgeConfig(upstream_base=self.fake.base, allow_http_downloads=True, task_timeout=30, poll_interval=3)

        def sleep(seconds):
            self.sleeps.append(seconds)
            self.clock[0] += seconds

        self.server = bridge.create_server(self.config, "127.0.0.1", 0, opener=DIRECT, sleep=sleep, clock=lambda: self.clock[0])
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.url = "http://127.0.0.1:%d" % self.server.server_address[1]

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.fake.close()

    def call(self, path="/v1beta/models/gemini-nano-banana-2.1:generateContent", body=None, headers=None):
        data = json.dumps(body if body is not None else gemini_body()).encode()
        request = urllib.request.Request(self.url + path, data=data, method="POST",
                                         headers=headers if headers is not None else {"x-goog-api-key": KEY, "Content-Type": "application/json"})
        try:
            with DIRECT.open(request, timeout=10) as response:
                return response.status, json.loads(response.read())
        except urllib.error.HTTPError as error:
            return error.code, json.loads(error.read())

    def test_answers_with_the_image_inline_after_polling(self):
        status, answer = self.call()

        self.assertEqual(200, status)
        part = answer["candidates"][0]["content"]["parts"][0]["inlineData"]
        self.assertEqual("image/png", part["mimeType"])  # sniffed: the link said octet-stream
        self.assertEqual(PNG, base64.b64decode(part["data"]))
        self.assertEqual("STOP", answer["candidates"][0]["finishReason"])
        self.assertEqual("gemini-nano-banana-2.1-ext", self.fake.submissions[0]["model"])
        self.assertEqual("2K", self.fake.submissions[0]["resolution"])
        # The channel key is forwarded as a bearer token to the API, and not to the file host.
        self.assertEqual(["Bearer " + KEY] * 3, self.fake.auth[:3])
        self.assertIsNone(self.fake.auth[3])
        self.assertEqual([3], self.sleeps)

    def test_key_may_come_as_bearer_or_query(self):
        self.assertEqual(200, self.call(headers={"Authorization": "Bearer " + KEY})[0])
        self.assertEqual(200, self.call(path="/v1beta/models/gemini-nano-banana-2.1:generateContent?key=" + KEY, headers={})[0])
        self.assertTrue(all(a == "Bearer " + KEY for a in self.fake.auth if a))

    def test_missing_key_unknown_path_and_streaming_are_refused(self):
        self.assertEqual(401, self.call(headers={})[0])
        self.assertEqual(404, self.call(path="/v1/chat/completions")[0])
        status, answer = self.call(path="/v1beta/models/gemini-nano-banana-2.1:streamGenerateContent")
        self.assertEqual(400, status)
        self.assertEqual("INVALID_ARGUMENT", answer["error"]["status"])
        self.assertEqual([], self.fake.submissions)

    def test_a_failed_task_about_the_request_is_a_client_error_with_its_message(self):
        self.fake.states = ["failed"]
        self.fake.failure = {"code": 400, "message": "内容安全拦截", "type": "invalid_request_error"}
        status, answer = self.call()
        self.assertEqual(400, status)
        self.assertEqual("内容安全拦截", answer["error"]["message"])

    def test_a_failed_task_on_the_upstream_side_is_a_server_error(self):
        self.fake.states = ["failed"]
        self.fake.failure = {"code": 500, "message": "upstream busy"}
        self.assertEqual(502, self.call()[0])

    def test_an_exhausted_upstream_balance_is_unavailable_not_a_user_problem(self):
        self.fake.submit_status = 402
        status, answer = self.call()
        self.assertEqual(503, status)
        self.assertIn("upstream account", answer["error"]["message"])

    def test_upstream_rate_limits_and_bad_keys_keep_their_meaning(self):
        self.fake.submit_status = 429
        self.assertEqual(429, self.call()[0])
        self.fake.submit_status = 401
        self.assertEqual(401, self.call()[0])

    def test_a_task_that_never_finishes_times_out(self):
        self.fake.states = ["processing"]
        status, answer = self.call()
        self.assertEqual(504, status)
        self.assertEqual("DEADLINE_EXCEEDED", answer["error"]["status"])
        self.assertEqual(10, len(self.sleeps))

    def test_health_check(self):
        with DIRECT.open(self.url + "/healthz", timeout=5) as response:
            self.assertEqual({"ok": True}, json.loads(response.read()))


class DownloadTests(unittest.TestCase):
    def test_plain_http_links_are_refused_by_default(self):
        client = bridge.ApimartClient(bridge.BridgeConfig(), DIRECT)
        with self.assertRaises(bridge.BridgeError) as caught:
            client.download("http://127.0.0.1:9/x.png")
        self.assertEqual(502, caught.exception.status)

    def test_mime_sniffing(self):
        self.assertEqual("image/jpeg", bridge.sniff_mime(b"\xff\xd8\xff\xe0"))
        self.assertEqual("image/webp", bridge.sniff_mime(b"RIFF\0\0\0\0WEBPVP8 "))


if __name__ == "__main__":
    unittest.main()
