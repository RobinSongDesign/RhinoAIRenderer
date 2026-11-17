import { describe, expect, it, vi } from 'vitest';
import { BridgeError } from './BridgeError';
import { HttpTransport, type EventSourceLike } from './httpTransport';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

class FakeEventSource implements EventSourceLike {
  readyState = 0;
  onopen: EventSourceLike['onopen'] = null;
  onerror: EventSourceLike['onerror'] = null;
  readonly listeners = new Map<string, ((event: MessageEvent) => void)[]>();
  closed = false;

  constructor(readonly url: string) {}

  addEventListener(type: string, listener: (event: MessageEvent) => void): void {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  close(): void {
    this.closed = true;
    this.readyState = 2;
  }

  open(): void {
    this.readyState = 1;
    this.onopen?.call(this as unknown as EventSource, new Event('open'));
  }

  fail(closed: boolean): void {
    this.readyState = closed ? 2 : 0;
    this.onerror?.call(this as unknown as EventSource, new Event('error'));
  }

  send(type: string, data: string): void {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(new MessageEvent(type, { data }));
    }
  }
}

describe('HttpTransport.call', () => {
  it('posts a JSON envelope and returns the result', async () => {
    const fetch = vi.fn(async () => jsonResponse({ id: '1', result: { ok: true } }));
    const transport = new HttpTransport({
      baseUrl: 'http://127.0.0.1:1/t',
      fetch,
      createEventSource: (u) => new FakeEventSource(u),
    });

    const result = await transport.call('providers.testKey', { id: 'BltAI' });

    expect(result).toEqual({ ok: true });
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('http://127.0.0.1:1/t/rpc');
    expect(JSON.parse(String(init.body))).toEqual({
      id: '1',
      method: 'providers.testKey',
      params: { id: 'BltAI' },
    });
  });

  it('omits params when none are given and maps a missing result to null', async () => {
    const fetch = vi.fn(async () => jsonResponse({ id: '1' }));
    const transport = new HttpTransport({ baseUrl: 'http://h/t/', fetch });

    expect(await transport.call('jobs.list', undefined)).toBeNull();
    const init = (fetch.mock.calls[0] as unknown as [string, RequestInit])[1];
    expect(JSON.parse(String(init.body))).not.toHaveProperty('params');
  });

  it('throws the host error as a BridgeError', async () => {
    const fetch = vi.fn(async () =>
      jsonResponse({
        id: '1',
        error: { code: 'auth_failed', message: 'bad', detail: 'provider text', retryable: false },
      }),
    );
    const transport = new HttpTransport({ baseUrl: 'http://h/t/', fetch });

    const error = await transport.call('providers.testKey', {}).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(BridgeError);
    expect(error).toMatchObject({ code: 'auth_failed', detail: 'provider text' });
  });

  it('reports network failures and HTTP errors as disconnected', async () => {
    const down = new HttpTransport({
      baseUrl: 'http://h/t/',
      fetch: vi.fn(async () => Promise.reject(new TypeError('fail'))),
    });
    const notFound = new HttpTransport({
      baseUrl: 'http://h/t/',
      fetch: vi.fn(async () => new Response('', { status: 404 })),
    });

    await expect(down.call('jobs.list', undefined)).rejects.toMatchObject({ code: 'disconnected' });
    await expect(notFound.call('jobs.list', undefined)).rejects.toMatchObject({ code: 'disconnected' });
  });
});

describe('HttpTransport events', () => {
  it('opens one stream lazily, dispatches parsed events and reports connection state', () => {
    const sources: FakeEventSource[] = [];
    const transport = new HttpTransport({
      baseUrl: 'http://h/t/',
      fetch: vi.fn(),
      createEventSource: (url) => {
        const source = new FakeEventSource(url);
        sources.push(source);
        return source;
      },
    });
    const states: string[] = [];
    const jobs: unknown[] = [];

    transport.onConnectionChange((state) => states.push(state));
    transport.on('job.updated', (data) => jobs.push(data));
    transport.on('settings.changed', () => undefined);
    sources[0].open();
    sources[0].send('job.updated', '{"id":"j1"}');
    sources[0].send('job.updated', 'not json');

    expect(sources).toHaveLength(1);
    expect(sources[0].url).toBe('http://h/t/events');
    expect(jobs).toEqual([{ id: 'j1' }]);
    expect(states).toEqual(['connecting', 'connected']);
  });

  it('reopens the stream after the server closed it, keeping subscriptions', () => {
    vi.useFakeTimers();
    try {
      const sources: FakeEventSource[] = [];
      const transport = new HttpTransport({
        baseUrl: 'http://h/t/',
        fetch: vi.fn(),
        reconnectDelayMs: 100,
        createEventSource: (url) => {
          const source = new FakeEventSource(url);
          sources.push(source);
          return source;
        },
      });
      const received: unknown[] = [];
      const states: string[] = [];
      transport.on('job.updated', (data) => received.push(data));
      transport.onConnectionChange((state) => states.push(state));

      sources[0].open();
      sources[0].fail(true);
      vi.advanceTimersByTime(100);
      sources[1].open();
      sources[1].send('job.updated', '{"id":"j2"}');

      expect(sources).toHaveLength(2);
      expect(received).toEqual([{ id: 'j2' }]);
      expect(states).toEqual(['connecting', 'connected', 'disconnected', 'connected']);
    } finally {
      vi.useRealTimers();
    }
  });

  it('builds asset URLs under the bridge root', () => {
    const transport = new HttpTransport({ baseUrl: 'http://h/t/', fetch: vi.fn() });
    expect(transport.assetUrl('abc')).toBe('http://h/t/assets/abc');
  });

  it('uploads with the blob type and maps rejected formats to invalid_request', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(
        jsonResponse({ assetId: 'a', mimeType: 'image/png', width: 1, height: 1, bytes: 3 }),
      )
      .mockResolvedValueOnce(new Response('', { status: 415 }));
    const transport = new HttpTransport({ baseUrl: 'http://h/t/', fetch });

    const stored = await transport.upload(new Blob(['abc'], { type: 'image/png' }));
    const init = (fetch.mock.calls[0] as unknown as [string, RequestInit])[1];

    expect(stored.assetId).toBe('a');
    expect((init.headers as Record<string, string>)['content-type']).toBe('image/png');
    await expect(transport.upload(new Blob(['x']))).rejects.toMatchObject({ code: 'invalid_request' });
  });
});
