import { BridgeError } from './BridgeError';
import type { ConnectionState, Transport } from './transport';
import type { AssetRef, RpcError } from './types';

type FetchLike = (input: string, init?: RequestInit) => Promise<Response>;

/** Minimal EventSource surface, so tests can substitute a fake. */
export interface EventSourceLike {
  readonly readyState: number;
  onopen: ((this: EventSource, ev: Event) => unknown) | null;
  onerror: ((this: EventSource, ev: Event) => unknown) | null;
  addEventListener(type: string, listener: (event: MessageEvent) => void): void;
  close(): void;
}

export interface HttpTransportOptions {
  /** Bridge root, ending in "/", e.g. http://127.0.0.1:5199/dev/. */
  baseUrl: string;
  fetch?: FetchLike;
  createEventSource?: (url: string) => EventSourceLike;
  /** Delay before re-opening an event stream the server closed. */
  reconnectDelayMs?: number;
}

const CLOSED = 2;

interface RpcEnvelope {
  id: string;
  result?: unknown;
  error?: RpcError;
}

/** Transport over the host's loopback HTTP server: POST /rpc, GET /events (SSE), /assets. */
export class HttpTransport implements Transport {
  private readonly baseUrl: string;
  private readonly fetchImpl: FetchLike;
  private readonly createEventSource: (url: string) => EventSourceLike;
  private readonly reconnectDelayMs: number;
  private readonly handlers = new Map<string, Set<(data: unknown) => void>>();
  private readonly connectionHandlers = new Set<(state: ConnectionState) => void>();
  private source: EventSourceLike | null = null;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private state: ConnectionState = 'connecting';
  private closed = false;
  private nextId = 1;

  constructor(options: HttpTransportOptions) {
    this.baseUrl = options.baseUrl.endsWith('/') ? options.baseUrl : options.baseUrl + '/';
    this.fetchImpl = options.fetch ?? ((input, init) => fetch(input, init));
    this.createEventSource = options.createEventSource ?? ((url) => new EventSource(url) as EventSourceLike);
    this.reconnectDelayMs = options.reconnectDelayMs ?? 2000;
  }

  async call(method: string, params: unknown, signal?: AbortSignal): Promise<unknown> {
    const body: Record<string, unknown> = { id: String(this.nextId++), method };
    if (params !== undefined) {
      body.params = params;
    }

    let response: Response;
    try {
      response = await this.fetchImpl(this.baseUrl + 'rpc', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(body),
        signal,
      });
    } catch (error) {
      if (signal?.aborted) {
        throw new BridgeError('cancelled', 'The call was aborted.');
      }
      throw new BridgeError('disconnected', 'The host could not be reached.', String(error));
    }

    if (!response.ok) {
      throw new BridgeError('disconnected', `The host answered HTTP ${response.status}.`);
    }

    const envelope = (await response.json()) as RpcEnvelope;
    if (envelope.error) {
      throw BridgeError.fromRpc(envelope.error);
    }
    return envelope.result ?? null;
  }

  on(event: string, handler: (data: unknown) => void): () => void {
    let set = this.handlers.get(event);
    if (!set) {
      set = new Set();
      this.handlers.set(event, set);
      this.source?.addEventListener(event, (message) => this.dispatch(event, message));
    }
    set.add(handler);
    this.ensureSource();
    return () => {
      set.delete(handler);
    };
  }

  onConnectionChange(handler: (state: ConnectionState) => void): () => void {
    this.connectionHandlers.add(handler);
    this.ensureSource();
    handler(this.state);
    return () => {
      this.connectionHandlers.delete(handler);
    };
  }

  assetUrl(assetId: string): string {
    return this.baseUrl + 'assets/' + encodeURIComponent(assetId);
  }

  async upload(blob: Blob): Promise<AssetRef> {
    let response: Response;
    try {
      response = await this.fetchImpl(this.baseUrl + 'assets', {
        method: 'POST',
        headers: { 'content-type': blob.type || 'application/octet-stream' },
        body: blob,
      });
    } catch (error) {
      throw new BridgeError('disconnected', 'The host could not be reached.', String(error));
    }

    if (response.status === 415) {
      throw new BridgeError('invalid_request', 'The file is not a PNG, JPEG or WebP image.');
    }
    if (response.status === 413) {
      throw new BridgeError('invalid_request', 'The image is too large.');
    }
    if (!response.ok) {
      throw new BridgeError('internal', `Upload failed with HTTP ${response.status}.`);
    }
    return (await response.json()) as AssetRef;
  }

  close(): void {
    this.closed = true;
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
    }
    this.source?.close();
    this.source = null;
  }

  private ensureSource(): void {
    if (this.source || this.closed) {
      return;
    }

    const source = this.createEventSource(this.baseUrl + 'events');
    this.source = source;
    for (const event of this.handlers.keys()) {
      source.addEventListener(event, (message) => this.dispatch(event, message));
    }
    source.onopen = () => this.setState('connected');
    source.onerror = () => {
      this.setState('disconnected');
      // The browser retries on network errors by itself, but gives up for good when the server
      // answered with an error status; recreate the stream in that case.
      if (source.readyState === CLOSED && !this.closed) {
        source.close();
        this.source = null;
        this.reconnectTimer = setTimeout(() => {
          this.reconnectTimer = null;
          this.ensureSource();
        }, this.reconnectDelayMs);
      }
    };
  }

  private dispatch(event: string, message: MessageEvent): void {
    let data: unknown;
    try {
      data = JSON.parse(String(message.data));
    } catch {
      return;
    }
    for (const handler of this.handlers.get(event) ?? []) {
      handler(data);
    }
  }

  private setState(state: ConnectionState): void {
    if (this.state === state) {
      return;
    }
    this.state = state;
    for (const handler of this.connectionHandlers) {
      handler(state);
    }
  }
}

/** Bridge root for the current page: the page's own folder, or VITE_BRIDGE_URL during `pnpm dev`. */
export function resolveBridgeBaseUrl(): string {
  const override = import.meta.env.VITE_BRIDGE_URL as string | undefined;
  return override && override.length > 0 ? override : new URL('.', window.location.href).href;
}
