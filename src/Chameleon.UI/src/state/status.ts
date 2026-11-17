import type { RpcError } from '../bridge/types';
import { BridgeError } from '../bridge/BridgeError';
import type { MessageKey, MessageParams } from '../i18n';

/** A line for the status bar: a translated message, optionally with an error to explain. */
export interface StatusMessage {
  kind: 'info' | 'busy' | 'success' | 'error';
  key: MessageKey;
  params?: MessageParams;
  error?: RpcError;
}

export const status = {
  info: (key: MessageKey, params?: MessageParams): StatusMessage => ({ kind: 'info', key, params }),
  busy: (key: MessageKey, params?: MessageParams): StatusMessage => ({ kind: 'busy', key, params }),
  success: (key: MessageKey, params?: MessageParams): StatusMessage => ({ kind: 'success', key, params }),
  /** An error whose message comes from its code (error.<code>). */
  error: (error: unknown): StatusMessage => {
    const info = BridgeError.from(error).toInfo();
    return { kind: 'error', key: `error.${info.code}` as MessageKey, error: info };
  },
};
