import type { AssetRef } from './types';

/** Connection state of the event channel. */
export type ConnectionState = 'connecting' | 'connected' | 'disconnected';

/**
 * How the UI reaches the host. Business code never uses this directly; it goes through
 * {@link Bridge}. Implementations: loopback HTTP (default) and, if ever needed, the Eto
 * message channel (see docs/plan/bridge-protocol.md section 1.2).
 */
export interface Transport {
  /** Sends an RPC call and resolves with its result; rejects with a BridgeError. */
  call(method: string, params: unknown, signal?: AbortSignal): Promise<unknown>;
  /** Subscribes to a host event; returns the unsubscribe function. */
  on(event: string, handler: (data: unknown) => void): () => void;
  /** Observes the event channel; the handler is called immediately with the current state. */
  onConnectionChange(handler: (state: ConnectionState) => void): () => void;
  /** URL the browser can load an asset from. */
  assetUrl(assetId: string): string;
  /** Uploads image bytes and returns the stored asset. */
  upload(blob: Blob): Promise<AssetRef>;
  /** Closes the event channel. */
  close(): void;
}
