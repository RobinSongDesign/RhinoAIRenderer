#!/usr/bin/env python3
"""
Gemini-to-APIMart bridge for the Chameleon relay (phase P4-R).

APIMart (https://apimart.ai) serves Gemini image models only through its own asynchronous
task API: submit a job, poll it, download the image from a short-lived link. The plug-in and
new-api speak Google's synchronous `:generateContent` format. This service sits between them:
a new-api channel of type Gemini points at it, and for each request it submits an APIMart
task, waits for it and answers with the image inline, as Google would. new-api keeps doing
accounts and per-size billing; the plug-in needs no change.

The channel key configured in new-api (sent as `x-goog-api-key`) is the APIMart key; it is
forwarded as a bearer token and never stored or logged. Prompts are not logged either.

Standard library only, so it runs in a stock python:3.12-alpine container without a build.
Configuration comes from environment variables, see BridgeConfig.from_env.
"""
from __future__ import annotations

import base64
import json
import logging
import os
import re
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Callable, Mapping, Optional

LOG = logging.getLogger("apimart-bridge")

# Output tiers in increasing size, as Gemini's imageConfig.imageSize spells them.
RESOLUTIONS = ("0.5K", "1K", "2K", "4K")


def normalize_resolution(value: Optional[str]) -> Optional[str]:
    """Maps the spellings clients use ("1k", "512", "4K") onto RESOLUTIONS; None if unknown."""
    if not value:
        return None
    text = str(value).strip().upper()
    if text in ("512", "0.5K"):
        return "0.5K"
    return text if text in RESOLUTIONS else None


@dataclass(frozen=True)
class ModelRoute:
    """Which APIMart model serves a model id the plug-in asks for, and what it can output."""

    upstream: str
    resolutions: tuple = ("1K", "2K", "4K")
    # Only APIMart's cheaper "Ext" models can fall back to the official channel.
    supports_fallback: bool = True

    def resolution_for(self, requested: Optional[str]) -> str:
        """The smallest supported tier at least as large as the request, else the largest one.

        Rounding up keeps the quality the user paid for; asking for more than the model can do
        gets its best instead of an error.
        """
        wanted = normalize_resolution(requested) or "1K"
        for tier in RESOLUTIONS[RESOLUTIONS.index(wanted):]:
            if tier in self.resolutions:
                return tier
        return self.resolutions[-1]


# Model ids of the plug-in's catalog, mapped to APIMart's per-image ("Ext") models.
DEFAULT_ROUTES: Mapping[str, ModelRoute] = {
    "gemini-nano-banana-2.1": ModelRoute("gemini-nano-banana-2.1-ext"),
    "gemini-3.1-flash-image": ModelRoute("gemini-3.1-flash-image-preview", ("0.5K", "1K", "2K", "4K")),
    "gemini-3-pro-image": ModelRoute("gemini-3-pro-image-preview"),
    "gemini-3.1-flash-lite-image": ModelRoute("gemini-3.1-flash-lite-image-ext", ("1K",), supports_fallback=False),
}


@dataclass(frozen=True)
class BridgeConfig:
    """Settings of the bridge; every field has an environment variable (see from_env)."""

    upstream_base: str = "https://api.apimart.ai"
    poll_interval: float = 3.0
    # Below the plug-in's five-minute limit per attempt, so a slow task fails here (unbilled)
    # instead of being billed by new-api after the plug-in has given up.
    task_timeout: float = 240.0
    request_timeout: float = 120.0
    official_fallback: bool = False
    error_language: str = "zh"
    max_request_bytes: int = 200 * 1024 * 1024
    max_image_bytes: int = 60 * 1024 * 1024
    # Result links must be https; tests against a local fake turn this on.
    allow_http_downloads: bool = False
    routes: Mapping[str, ModelRoute] = field(default_factory=lambda: dict(DEFAULT_ROUTES))

    def route_for(self, model: str) -> ModelRoute:
        """The route of a model; an unknown id goes to APIMart unchanged."""
        return self.routes.get(model) or ModelRoute(model, RESOLUTIONS, supports_fallback=False)

    @classmethod
    def from_env(cls, env: Mapping[str, str]) -> "BridgeConfig":
        """Reads APIMART_BASE_URL, BRIDGE_POLL_SECONDS, BRIDGE_TASK_TIMEOUT_SECONDS,
        BRIDGE_OFFICIAL_FALLBACK, BRIDGE_ERROR_LANGUAGE and BRIDGE_MODEL_MAP (JSON:
        {"model": {"upstream": "...", "resolutions": ["1K", ...], "supportsFallback": true}})."""
        routes = dict(DEFAULT_ROUTES)
        raw_map = env.get("BRIDGE_MODEL_MAP", "").strip()
        if raw_map:
            for model, spec in json.loads(raw_map).items():
                resolutions = tuple(normalize_resolution(r) for r in spec.get("resolutions", ("1K", "2K", "4K")))
                if not resolutions or None in resolutions:
                    raise ValueError("BRIDGE_MODEL_MAP: unknown resolution for " + model)
                routes[model] = ModelRoute(spec["upstream"], resolutions, bool(spec.get("supportsFallback", True)))
        return cls(
            upstream_base=env.get("APIMART_BASE_URL", cls.upstream_base).rstrip("/"),
            poll_interval=float(env.get("BRIDGE_POLL_SECONDS", cls.poll_interval)),
            task_timeout=float(env.get("BRIDGE_TASK_TIMEOUT_SECONDS", cls.task_timeout)),
            official_fallback=env.get("BRIDGE_OFFICIAL_FALLBACK", "false").strip().lower() in ("1", "true", "yes"),
            error_language=env.get("BRIDGE_ERROR_LANGUAGE", cls.error_language).strip(),
            routes=routes,
        )


class BridgeError(Exception):
    """A failure to report to new-api in Google's error format.

    The HTTP status matters: new-api does not bill failed requests, and treats 5xx as a reason
    to try another channel.
    """

    def __init__(self, status: int, reason: str, message: str):
        super().__init__(message)
        self.status = status
        self.reason = reason
        self.message = message

    def to_json(self) -> dict:
        return {"error": {"code": self.status, "message": self.message, "status": self.reason}}


@dataclass(frozen=True)
class ImageTask:
    """One APIMart image generation request."""

    model: str
    prompt: str
    resolution: str
    size: Optional[str] = None
    image_urls: tuple = ()
    official_fallback: bool = False

    def to_json(self) -> dict:
        body = {"model": self.model, "prompt": self.prompt, "resolution": self.resolution, "n": 1}
        if self.size:
            body["size"] = self.size
        if self.image_urls:
            body["image_urls"] = list(self.image_urls)
        if self.official_fallback:
            body["official_fallback"] = True
        return body


class GeminiRequestTranslator:
    """Turns a Gemini generateContent body into an APIMart task."""

    def __init__(self, config: BridgeConfig):
        self._config = config

    def translate(self, model: str, body: dict) -> ImageTask:
        texts = []
        images = []
        instruction = body.get("systemInstruction") or body.get("system_instruction") or {}
        for part in instruction.get("parts", []) if isinstance(instruction, dict) else []:
            self._collect(part, texts, images)
        for content in body.get("contents", []) or []:
            for part in (content or {}).get("parts", []) or []:
                self._collect(part, texts, images)

        prompt = "\n\n".join(t for t in texts if t.strip())
        if not prompt:
            raise BridgeError(400, "INVALID_ARGUMENT", "The request has no text prompt.")

        generation = body.get("generationConfig") or body.get("generation_config") or {}
        image_config = generation.get("imageConfig") or generation.get("image_config") or {}
        route = self._config.route_for(model)
        aspect = image_config.get("aspectRatio") or image_config.get("aspect_ratio")
        return ImageTask(
            model=route.upstream,
            prompt=prompt,
            resolution=route.resolution_for(image_config.get("imageSize") or image_config.get("image_size")),
            # No ratio: APIMart follows the first image, which is what "keep the original ratio" means.
            size=str(aspect) if aspect else None,
            image_urls=tuple(images),
            official_fallback=self._config.official_fallback and route.supports_fallback,
        )

    @staticmethod
    def _collect(part: dict, texts: list, images: list) -> None:
        # Images keep their order: the plug-in's prompt refers to them by position.
        if not isinstance(part, dict):
            return
        if isinstance(part.get("text"), str):
            texts.append(part["text"])
        inline = part.get("inlineData") or part.get("inline_data")
        if isinstance(inline, dict) and inline.get("data"):
            mime = inline.get("mimeType") or inline.get("mime_type") or "image/png"
            images.append("data:" + mime + ";base64," + inline["data"])
        file_data = part.get("fileData") or part.get("file_data")
        if isinstance(file_data, dict) and str(file_data.get("fileUri", "")).startswith("https://"):
            images.append(file_data["fileUri"])


class ApimartClient:
    """APIMart's task API: submit, query, download. Every call carries the caller's key."""

    def __init__(self, config: BridgeConfig, opener: Optional[urllib.request.OpenerDirector] = None):
        self._config = config
        self._opener = opener or urllib.request.build_opener()

    def submit(self, task: ImageTask, key: str) -> str:
        answer = self._json("POST", "/v1/images/generations", key, task.to_json())
        items = answer.get("data") or []
        task_id = items[0].get("task_id") if items and isinstance(items[0], dict) else None
        if not task_id:
            raise BridgeError(502, "UNAVAILABLE", "APIMart accepted the task without a task id.")
        return task_id

    def status(self, task_id: str, key: str) -> dict:
        query = "?language=" + urllib.parse.quote(self._config.error_language) if self._config.error_language else ""
        return self._json("GET", "/v1/tasks/" + urllib.parse.quote(task_id) + query, key).get("data") or {}

    def download(self, url: str) -> tuple:
        """Fetches a result image; returns (bytes, mime type)."""
        scheme = urllib.parse.urlsplit(url).scheme
        if scheme != "https" and not (scheme == "http" and self._config.allow_http_downloads):
            raise BridgeError(502, "UNAVAILABLE", "APIMart returned an image link that is not https.")
        try:
            with self._opener.open(urllib.request.Request(url), timeout=self._config.request_timeout) as response:
                data = response.read(self._config.max_image_bytes + 1)
                mime = (response.headers.get_content_type() or "").lower()
        except (urllib.error.URLError, OSError) as error:
            raise BridgeError(502, "UNAVAILABLE", "Downloading the image from APIMart failed: " + str(error)) from error
        if len(data) > self._config.max_image_bytes:
            raise BridgeError(502, "UNAVAILABLE", "The image from APIMart is too large.")
        return data, mime if mime.startswith("image/") else sniff_mime(data)

    def _json(self, method: str, path: str, key: str, body: Optional[dict] = None) -> dict:
        request = urllib.request.Request(
            self._config.upstream_base + path,
            method=method,
            data=json.dumps(body).encode("utf-8") if body is not None else None,
            headers={"Authorization": "Bearer " + key, "Content-Type": "application/json", "Accept": "application/json"},
        )
        try:
            with self._opener.open(request, timeout=self._config.request_timeout) as response:
                return json.loads(response.read() or b"{}")
        except urllib.error.HTTPError as error:
            raise upstream_error(error.code, error.read()) from None
        except (urllib.error.URLError, OSError, ValueError) as error:
            raise BridgeError(502, "UNAVAILABLE", "APIMart is unreachable: " + str(error)) from error


def upstream_error(status: int, payload: bytes) -> BridgeError:
    """Translates an APIMart HTTP error into what new-api and the plug-in should see."""
    message = "APIMart returned HTTP " + str(status) + "."
    try:
        detail = json.loads(payload or b"{}").get("error") or {}
        message = detail.get("message") or message
    except (ValueError, AttributeError):
        pass
    if status in (401, 403):
        return BridgeError(401, "UNAUTHENTICATED", message)
    if status == 402:
        # The operator's APIMart balance ran out: not the user's fault, and not their balance.
        return BridgeError(503, "UNAVAILABLE", "The upstream account is out of balance: " + message)
    if status == 429:
        return BridgeError(429, "RESOURCE_EXHAUSTED", message)
    if 400 <= status < 500:
        return BridgeError(400, "INVALID_ARGUMENT", message)
    return BridgeError(502, "UNAVAILABLE", message)


def sniff_mime(data: bytes) -> str:
    """Image type from the file signature, for links served without a useful Content-Type."""
    if data.startswith(b"\x89PNG"):
        return "image/png"
    if data.startswith(b"\xff\xd8"):
        return "image/jpeg"
    if data[:4] == b"RIFF" and data[8:12] == b"WEBP":
        return "image/webp"
    return "application/octet-stream"


class TaskRunner:
    """Submits a task and waits for its image. Clock and sleep are injectable for tests."""

    def __init__(self, client: ApimartClient, config: BridgeConfig,
                 sleep: Callable[[float], None] = time.sleep, clock: Callable[[], float] = time.monotonic):
        self._client = client
        self._config = config
        self._sleep = sleep
        self._clock = clock

    def run(self, task: ImageTask, key: str) -> tuple:
        task_id = self._client.submit(task, key)
        deadline = self._clock() + self._config.task_timeout
        while True:
            data = self._client.status(task_id, key)
            state = data.get("status")
            if state == "completed":
                return self._client.download(first_image_url(data, task_id))
            if state in ("failed", "cancelled"):
                error = data.get("error") or {}
                code = error.get("code") if isinstance(error.get("code"), int) else 500
                message = error.get("message") or "The image task " + str(state) + "."
                # A 4xx-class failure is about the request (content policy, bad image): the user can fix it.
                if 400 <= code < 500:
                    raise BridgeError(400, "INVALID_ARGUMENT", message)
                raise BridgeError(502, "UNAVAILABLE", message)
            if self._clock() >= deadline:
                raise BridgeError(504, "DEADLINE_EXCEEDED", "The image task " + task_id + " did not finish in time.")
            self._sleep(self._config.poll_interval)


def first_image_url(data: dict, task_id: str) -> str:
    images = (data.get("result") or {}).get("images") or []
    url = images[0].get("url") if images and isinstance(images[0], dict) else None
    if isinstance(url, list):
        url = url[0] if url else None
    if not isinstance(url, str) or not url:
        raise BridgeError(502, "UNAVAILABLE", "The image task " + task_id + " finished without an image.")
    return url


def gemini_image_response(image: bytes, mime: str, model: str) -> dict:
    """A Gemini generateContent answer carrying one inline image.

    The token counts are nominal (what Google reports for one 1K image): APIMart bills per
    image, and new-api must price these models per request with a billing expression. They
    only keep a token-priced setup from charging nothing.
    """
    return {
        "candidates": [{
            "content": {"role": "model", "parts": [{"inlineData": {"mimeType": mime, "data": base64.b64encode(image).decode("ascii")}}]},
            "finishReason": "STOP",
            "index": 0,
        }],
        "usageMetadata": {"promptTokenCount": 1290, "candidatesTokenCount": 1290, "totalTokenCount": 2580},
        "modelVersion": model,
    }


class Bridge:
    """Handles one generateContent call, independent of the HTTP server."""

    def __init__(self, config: BridgeConfig, runner: TaskRunner):
        self._translator = GeminiRequestTranslator(config)
        self._runner = runner

    def generate(self, model: str, body: dict, key: str) -> dict:
        task = self._translator.translate(model, body)
        started = time.monotonic()
        image, mime = self._runner.run(task, key)
        LOG.info("model=%s upstream=%s resolution=%s images_in=%d bytes_out=%d seconds=%.1f",
                 model, task.model, task.resolution, len(task.image_urls), len(image), time.monotonic() - started)
        return gemini_image_response(image, mime, task.model)


_GENERATE_PATH = re.compile(r"^/v1(?:beta|alpha)?/models/([^/:]+):(generateContent|streamGenerateContent)$")


def make_handler(bridge: Bridge, config: BridgeConfig):
    """The HTTP handler class bound to a bridge."""

    class Handler(BaseHTTPRequestHandler):
        server_version = "chameleon-apimart-bridge"
        protocol_version = "HTTP/1.1"

        def log_message(self, fmt, *args):
            # Request lines carry ?key= when a client sends the key that way: never log them.
            pass

        def do_GET(self):
            if urllib.parse.urlsplit(self.path).path == "/healthz":
                self._reply(200, {"ok": True})
            else:
                self._reply(404, BridgeError(404, "NOT_FOUND", "Not found.").to_json())

        def do_POST(self):
            parts = urllib.parse.urlsplit(self.path)
            match = _GENERATE_PATH.match(parts.path)
            try:
                if not match:
                    raise BridgeError(404, "NOT_FOUND", "Only :generateContent is served here.")
                if match.group(2) != "generateContent":
                    raise BridgeError(400, "INVALID_ARGUMENT", "Streaming is not supported for image models.")
                key = self._key(parts.query)
                body = self._body()
                self._reply(200, bridge.generate(urllib.parse.unquote(match.group(1)), body, key))
            except BridgeError as error:
                LOG.warning("path=%s status=%d %s", parts.path, error.status, error.message)
                self._reply(error.status, error.to_json())
            except Exception:  # noqa: BLE001 - the server must answer, whatever went wrong.
                LOG.exception("unexpected failure")
                self._reply(500, BridgeError(500, "INTERNAL", "Internal bridge error.").to_json())

        def _key(self, query: str) -> str:
            key = self.headers.get("x-goog-api-key", "").strip()
            auth = self.headers.get("Authorization", "")
            if not key and auth.lower().startswith("bearer "):
                key = auth[7:].strip()
            if not key:
                key = (urllib.parse.parse_qs(query).get("key") or [""])[0].strip()
            if not key:
                raise BridgeError(401, "UNAUTHENTICATED", "Set the APIMart key as the channel key.")
            return key

        def _body(self) -> dict:
            length = int(self.headers.get("Content-Length") or 0)
            if length <= 0 or length > config.max_request_bytes:
                raise BridgeError(400, "INVALID_ARGUMENT", "Missing or oversized request body.")
            try:
                body = json.loads(self.rfile.read(length))
            except ValueError:
                raise BridgeError(400, "INVALID_ARGUMENT", "The request body is not JSON.") from None
            if not isinstance(body, dict):
                raise BridgeError(400, "INVALID_ARGUMENT", "The request body must be a JSON object.")
            return body

        def _reply(self, status: int, payload: dict) -> None:
            data = json.dumps(payload).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

    return Handler


def create_server(config: BridgeConfig, host: str, port: int,
                  opener: Optional[urllib.request.OpenerDirector] = None,
                  sleep: Callable[[float], None] = time.sleep,
                  clock: Callable[[], float] = time.monotonic) -> ThreadingHTTPServer:
    """Wires the bridge into a threaded HTTP server (one thread per waiting request)."""
    runner = TaskRunner(ApimartClient(config, opener), config, sleep, clock)
    server = ThreadingHTTPServer((host, port), make_handler(Bridge(config, runner), config))
    server.daemon_threads = True
    return server


def main() -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    config = BridgeConfig.from_env(os.environ)
    host = os.environ.get("BRIDGE_HOST", "0.0.0.0")
    port = int(os.environ.get("BRIDGE_PORT", "8080"))
    server = create_server(config, host, port)
    LOG.info("listening on %s:%d, upstream %s, models %s", host, port, config.upstream_base, ", ".join(sorted(config.routes)))
    server.serve_forever()


if __name__ == "__main__":
    main()
