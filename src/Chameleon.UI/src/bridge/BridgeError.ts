import type { ErrorCode, RpcError } from './types';

/** A failed bridge call. `code` drives the localized message; `detail` is shown on demand. */
export class BridgeError extends Error {
  readonly code: ErrorCode;
  readonly detail?: string;
  readonly retryable: boolean;

  constructor(code: ErrorCode, message: string, detail?: string, retryable = false) {
    super(message);
    this.name = 'BridgeError';
    this.code = code;
    this.detail = detail;
    this.retryable = retryable;
  }

  /** Builds an error from the `error` member of an RPC response or a failed job. */
  static fromRpc(error: RpcError): BridgeError {
    return new BridgeError(error.code, error.message, error.detail, error.retryable ?? false);
  }

  /** Normalizes anything thrown into a BridgeError. */
  static from(error: unknown): BridgeError {
    if (error instanceof BridgeError) {
      return error;
    }
    const message = error instanceof Error ? error.message : String(error);
    return new BridgeError('internal', message);
  }

  /** Plain-data form, for storing in UI state. */
  toInfo(): RpcError {
    return { code: this.code, message: this.message, detail: this.detail, retryable: this.retryable };
  }
}
