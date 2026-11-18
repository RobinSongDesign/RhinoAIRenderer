#!/usr/bin/env python3
"""
Fake image-model upstream for checking a relay gateway (such as new-api) end to end.

It answers Gemini `:generateContent` and OpenAI `/v1/images/*` requests with a fixed PNG and
records, one JSON line per request, what actually arrived: path, auth style, size, the image
parts and the generation config. Point a relay channel at it, send real plug-in requests
through the relay (e.g. with tools/Chameleon.Cli), then compare the log with what was sent.

A prompt containing "#status:NNN" makes the upstream fail with that HTTP status, to check how
the relay passes errors on.

Usage: fake_image_upstream.py <port> <log.jsonl> <result.png>
Binds to 127.0.0.1 only. Never use real keys with it; it ignores them.
"""
import base64
import json
import re
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


def summarize_gemini(body):
    """Image parts (type and decoded size), text heads and the generation config."""
    contents = []
    for content in body.get('contents', []):
        parts = []
        for part in content.get('parts', []):
            data = part.get('inline_data') or part.get('inlineData')
            if data:
                parts.append({'image': data.get('mime_type') or data.get('mimeType'),
                              'bytes': len(base64.b64decode(data.get('data', '')))})
            elif 'text' in part:
                parts.append({'text': part['text'][:60]})
            else:
                parts.append({'other': sorted(part.keys())})
        contents.append({'role': content.get('role'), 'parts': parts})
    return {'contents': contents,
            'generationConfig': body.get('generationConfig'),
            'systemInstruction': bool(body.get('systemInstruction') or body.get('system_instruction'))}


def make_handler(log_path, png_base64):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass

        def reply(self, status, payload):
            data = json.dumps(payload).encode()
            self.send_response(status)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_GET(self):
            self.record(b'')
            if self.path.startswith(('/v1beta/models', '/v1/models')):
                self.reply(200, {'models': [], 'data': []})
            else:
                self.reply(404, {'error': {'message': 'not found'}})

        def do_POST(self):
            body = self.rfile.read(int(self.headers.get('Content-Length') or 0))
            self.record(body)
            forced = re.search(rb'#status:(\d{3})', body)
            if forced:
                code = int(forced.group(1))
                self.reply(code, {'error': {'code': code, 'message': 'upstream says %d' % code, 'status': 'FORCED'}})
            elif ':generateContent' in self.path:
                self.reply(200, {
                    'candidates': [{'content': {'role': 'model', 'parts': [
                        {'text': 'done'}, {'inlineData': {'mimeType': 'image/png', 'data': png_base64}}]},
                        'finishReason': 'STOP'}],
                    'usageMetadata': {'promptTokenCount': 1290, 'candidatesTokenCount': 1290, 'totalTokenCount': 2580},
                })
            elif self.path.startswith('/v1/images/'):
                self.reply(200, {'created': 1, 'data': [{'b64_json': png_base64}],
                                 'usage': {'input_tokens': 100, 'output_tokens': 1000, 'total_tokens': 1100}})
            else:
                self.reply(404, {'error': {'message': 'not found'}})

        def record(self, body):
            entry = {'method': self.command, 'path': self.path,
                     # Only the scheme of the credential, never the credential itself.
                     'auth': (self.headers.get('Authorization') or '').split(' ')[0],
                     'googKey': bool(self.headers.get('x-goog-api-key')),
                     'contentType': self.headers.get('Content-Type'), 'bytes': len(body)}
            content_type = self.headers.get('Content-Type') or ''
            if body and 'json' in content_type:
                try:
                    parsed = json.loads(body)
                    if 'contents' in parsed:
                        entry['gemini'] = summarize_gemini(parsed)
                    else:
                        entry['fields'] = sorted(parsed.keys())
                except ValueError:
                    entry['json'] = 'unparseable'
            elif body:
                entry['multipartFields'] = sorted({m.decode() for m in re.findall(rb'name="([^"]+)"', body)})
            with open(log_path, 'a') as log:
                log.write(json.dumps(entry) + '\n')

    return Handler


def main():
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    port, log_path, png_path = int(sys.argv[1]), sys.argv[2], sys.argv[3]
    with open(png_path, 'rb') as png:
        png_base64 = base64.b64encode(png.read()).decode()
    ThreadingHTTPServer(('127.0.0.1', port), make_handler(log_path, png_base64)).serve_forever()


if __name__ == '__main__':
    main()
