import { BridgeError } from './BridgeError';
import type { ConnectionState, Transport } from './transport';
import type { AssetRef, BridgeEventName, BridgeEvents, RpcMethod, RpcParams, RpcResult } from './types';

type CallArgs<M extends RpcMethod> =
  RpcParams<M> extends void
    ? [params?: undefined, signal?: AbortSignal]
    : [params: RpcParams<M>, signal?: AbortSignal];

/** Typed client of the host. The only way the rest of the UI talks to the host. */
export class Bridge {
  private readonly transport: Transport;

  constructor(transport: Transport) {
    this.transport = transport;
  }

  /** Calls a host method. Rejects with {@link BridgeError}. */
  async call<M extends RpcMethod>(method: M, ...args: CallArgs<M>): Promise<RpcResult<M>> {
    const [params, signal] = args;
    try {
      return (await this.transport.call(method, params, signal)) as RpcResult<M>;
    } catch (error) {
      throw BridgeError.from(error);
    }
  }

  /** Subscribes to a host event; returns the unsubscribe function. */
  on<E extends BridgeEventName>(event: E, handler: (data: BridgeEvents[E]) => void): () => void {
    return this.transport.on(event, (data) => handler(data as BridgeEvents[E]));
  }

  /** Observes the connection to the host. */
  onConnectionChange(handler: (state: ConnectionState) => void): () => void {
    return this.transport.onConnectionChange(handler);
  }

  /** URL an `<img>` can load the asset from. */
  assetUrl(assetId: string): string {
    return this.transport.assetUrl(assetId);
  }

  /** Stores image bytes on the host. */
  async upload(blob: Blob): Promise<AssetRef> {
    try {
      return await this.transport.upload(blob);
    } catch (error) {
      throw BridgeError.from(error);
    }
  }

  /** Releases the event channel. */
  close(): void {
    this.transport.close();
  }
}
