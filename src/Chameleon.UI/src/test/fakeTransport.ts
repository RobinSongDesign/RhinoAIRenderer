import { BridgeError } from '../bridge/BridgeError';
import type { ConnectionState, Transport } from '../bridge/transport';
import type { AssetRef, RpcMethod } from '../bridge/types';

type Handler = (params: unknown) => unknown;

/**
 * In-memory transport for unit tests: RPC methods are answered by registered handlers and
 * events are emitted by the test.
 */
export class FakeTransport implements Transport {
  readonly calls: { method: string; params: unknown }[] = [];
  readonly uploads: Blob[] = [];
  private readonly handlers = new Map<string, Handler>();
  private readonly listeners = new Map<string, Set<(data: unknown) => void>>();
  private readonly connectionListeners = new Set<(state: ConnectionState) => void>();
  private connection: ConnectionState = 'connecting';

  handle(method: RpcMethod, handler: Handler): this {
    this.handlers.set(method, handler);
    return this;
  }

  async call(method: string, params: unknown): Promise<unknown> {
    this.calls.push({ method, params });
    const handler = this.handlers.get(method);
    if (!handler) {
      throw new BridgeError('invalid_request', `No fake handler for ${method}`);
    }
    return handler(params);
  }

  on(event: string, handler: (data: unknown) => void): () => void {
    const set = this.listeners.get(event) ?? new Set();
    set.add(handler);
    this.listeners.set(event, set);
    return () => set.delete(handler);
  }

  emit(event: string, data: unknown): void {
    for (const handler of this.listeners.get(event) ?? []) {
      handler(data);
    }
  }

  onConnectionChange(handler: (state: ConnectionState) => void): () => void {
    this.connectionListeners.add(handler);
    handler(this.connection);
    return () => this.connectionListeners.delete(handler);
  }

  setConnection(state: ConnectionState): void {
    this.connection = state;
    for (const handler of this.connectionListeners) {
      handler(state);
    }
  }

  assetUrl(assetId: string): string {
    return `asset://${assetId}`;
  }

  async upload(blob: Blob): Promise<AssetRef> {
    this.uploads.push(blob);
    return {
      assetId: `uploaded-${this.uploads.length}`,
      mimeType: blob.type,
      width: 1,
      height: 1,
      bytes: blob.size,
    };
  }

  close(): void {}

  callsOf(method: RpcMethod): unknown[] {
    return this.calls.filter((c) => c.method === method).map((c) => c.params);
  }
}

/** Waits until queued promise callbacks have run. */
export function flush(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}
