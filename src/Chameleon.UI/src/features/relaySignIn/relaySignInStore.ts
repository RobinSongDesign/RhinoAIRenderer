import { createStore, type StoreApi } from 'zustand/vanilla';
import type { Bridge } from '../../bridge/Bridge';
import { BridgeError } from '../../bridge/BridgeError';
import type { RpcError, SignInResult } from '../../bridge/types';

/** Pages of a new-api website the sign-in dialog links to. */
export type AccountPage = 'register' | 'forgotPassword';

const PAGE_PATHS: Record<AccountPage, string> = {
  register: '/sign-up',
  forgotPassword: '/forgot-password',
};

/** Address of an account page on the provider's website. */
export function accountPageUrl(baseUrl: string, page: AccountPage): string {
  return baseUrl.replace(/\/+$/, '') + PAGE_PATHS[page];
}

/** The provider being signed in to. */
export interface SignInTarget {
  providerId: string;
  displayName: string;
  baseUrl: string;
}

export type SignInStep = 'credentials' | 'twoFactor';

export interface RelaySignInStoreState {
  /** Provider of the open dialog; null while it is closed. */
  target: SignInTarget | null;
  step: SignInStep;
  busy: boolean;
  error: RpcError | null;

  /** Opens the dialog for a provider that supports signing in. */
  open(providerId: string): Promise<void>;
  close(): void;
  /** Sends the credentials; resolves true once signed in (false: failed or a code is needed). */
  signIn(username: string, password: string): Promise<boolean>;
  /** Sends the two-factor code of a pending sign-in. */
  verify(code: string): Promise<boolean>;
  /** Forgets the provider's account key on this machine. */
  signOut(providerId: string): Promise<void>;
  openPage(page: AccountPage): Promise<void>;
}

export interface RelaySignInStoreDeps {
  bridge: Bridge;
  /** Called after the stored key changed, so views that cache provider data reload it. */
  onChanged?: () => void;
}

/**
 * Signing in to a relay account from the plug-in. The password only passes through
 * {@link RelaySignInStoreState.signIn}; it is never kept in state.
 */
export function createRelaySignInStore({ bridge, onChanged }: RelaySignInStoreDeps): StoreApi<RelaySignInStoreState> {
  // Handle of a pending two-factor step; not part of the rendered state.
  let flowToken: string | null = null;

  const store = createStore<RelaySignInStoreState>()((set, get) => ({
    target: null,
    step: 'credentials',
    busy: false,
    error: null,

    async open(providerId) {
      flowToken = null;
      set({ step: 'credentials', busy: true, error: null });
      try {
        const provider = (await bridge.call('providers.list')).find((p) => p.id === providerId);
        if (provider?.supportsSignIn) {
          set({ target: { providerId, displayName: provider.displayName, baseUrl: provider.baseUrl } });
        }
      } catch (error) {
        set({ error: BridgeError.from(error).toInfo() });
      } finally {
        set({ busy: false });
      }
    },

    close() {
      flowToken = null;
      set({ target: null, step: 'credentials', busy: false, error: null });
    },

    signIn(username, password) {
      const target = get().target;
      if (!target || !username.trim() || !password) {
        return Promise.resolve(false);
      }
      return submit(() => bridge.call('providers.signIn', { id: target.providerId, username: username.trim(), password }));
    },

    verify(code) {
      const target = get().target;
      if (!target || !flowToken || !code.trim()) {
        return Promise.resolve(false);
      }
      const token = flowToken;
      return submit(() => bridge.call('providers.signInVerify', { id: target.providerId, flowToken: token, code: code.trim() }));
    },

    async signOut(providerId) {
      try {
        await bridge.call('providers.signOut', { id: providerId });
        onChanged?.();
      } catch (error) {
        set({ error: BridgeError.from(error).toInfo() });
      }
    },

    async openPage(page) {
      const target = get().target;
      if (target) {
        await bridge
          .call('shell.openUrl', { url: accountPageUrl(target.baseUrl, page) })
          .catch((error: unknown) => set({ error: BridgeError.from(error).toInfo() }));
      }
    },
  }));

  async function submit(call: () => Promise<SignInResult>): Promise<boolean> {
    store.setState({ busy: true, error: null });
    try {
      const result = await call();
      if (result.status === 'needsTwoFactor') {
        flowToken = result.flowToken;
        store.setState({ step: 'twoFactor' });
        return false;
      }
      flowToken = null;
      store.setState({ target: null, step: 'credentials' });
      onChanged?.();
      return true;
    } catch (error) {
      store.setState({ error: BridgeError.from(error).toInfo() });
      return false;
    } finally {
      store.setState({ busy: false });
    }
  }

  return store;
}
