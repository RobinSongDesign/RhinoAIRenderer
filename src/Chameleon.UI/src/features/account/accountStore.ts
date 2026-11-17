import { createStore, type StoreApi } from 'zustand/vanilla';
import type { Bridge } from '../../bridge/Bridge';
import { BridgeError } from '../../bridge/BridgeError';
import { CLOUD_BACKEND_ID, type LedgerItem, type RpcError } from '../../bridge/types';
import type { MessageKey, MessageParams } from '../../i18n';
import type { AppStoreState } from '../../state/appStore';
import type { StatusMessage } from '../../state/status';

export type AccountDialog = 'login' | 'redeem' | 'ledger' | null;

export interface RedeemOutcome {
  kind: 'success' | 'error';
  key: MessageKey;
  params?: MessageParams;
}

export interface AccountStoreState {
  dialog: AccountDialog;
  menuOpen: boolean;
  busy: boolean;
  error: RpcError | null;
  redeemOutcome: RedeemOutcome | null;
  /** Shown above the redeem field when the dialog opened because credits ran out. */
  redeemReason: MessageKey | null;
  ledger: LedgerItem[];
  ledgerCursor: string | null;
  welcomeDismissed: boolean;

  toggleMenu(open?: boolean): void;
  startLogin(): Promise<void>;
  cancelLogin(): Promise<void>;
  signOut(): Promise<void>;
  openRedeem(reason?: MessageKey): void;
  redeem(code: string): Promise<void>;
  openLedger(): Promise<void>;
  loadMoreLedger(): Promise<void>;
  closeDialog(): void;
  dismissWelcome(): void;
}

export interface AccountStoreDeps {
  bridge: Bridge;
  app: StoreApi<AppStoreState>;
  /** Status sources watched for "insufficient credits" (single and batch mode). */
  statusSources: StoreApi<{ status: StatusMessage | null }>[];
}

/** Formats a redeem code as typed into groups of four (XXXX-XXXX-…). */
export function formatRedeemCode(input: string): string {
  const raw = input.toUpperCase().replace(/[^0-9A-Z]/g, '').slice(0, 16);
  return raw.replace(/(.{4})(?=.)/g, '$1-');
}

const redeemErrorKeys: Partial<Record<string, MessageKey>> = {
  not_found: 'redeem.notFound',
  conflict: 'redeem.used',
  gone: 'redeem.expired',
};

/** Account-mode interactions. The account itself lives in the app store (it comes with the app state). */
export function createAccountStore({ bridge, app, statusSources }: AccountStoreDeps): StoreApi<AccountStoreState> {
  const store = createStore<AccountStoreState>()((set, get) => ({
    dialog: null,
    menuOpen: false,
    busy: false,
    error: null,
    redeemOutcome: null,
    redeemReason: null,
    ledger: [],
    ledgerCursor: null,
    welcomeDismissed: false,

    toggleMenu: (open) => set((s) => ({ menuOpen: open ?? !s.menuOpen })),

    async startLogin() {
      set({ dialog: 'login', menuOpen: false, busy: true, error: null });
      try {
        await bridge.call('auth.startLogin');
      } catch (error) {
        set({ error: BridgeError.from(error).toInfo() });
      } finally {
        set({ busy: false });
      }
    },

    async cancelLogin() {
      set({ dialog: null });
      await bridge.call('auth.cancelLogin').catch(() => undefined);
    },

    async signOut() {
      set({ menuOpen: false });
      await bridge.call('auth.logout').catch(() => undefined);
    },

    openRedeem: (reason) =>
      set({ dialog: 'redeem', menuOpen: false, redeemOutcome: null, redeemReason: reason ?? null }),

    async redeem(code) {
      set({ busy: true, redeemOutcome: null });
      try {
        const result = await bridge.call('account.redeem', { code });
        set({
          redeemOutcome: {
            kind: 'success',
            key: 'redeem.success',
            params: { added: result.creditsAdded, credits: result.credits },
          },
          redeemReason: null,
        });
      } catch (error) {
        const info = BridgeError.from(error).toInfo();
        set({ redeemOutcome: { kind: 'error', key: redeemErrorKeys[info.code] ?? (`error.${info.code}` as MessageKey) } });
      } finally {
        set({ busy: false });
      }
    },

    async openLedger() {
      set({ dialog: 'ledger', menuOpen: false, ledger: [], ledgerCursor: null, busy: true, error: null });
      try {
        const page = await bridge.call('account.ledger');
        set({ ledger: page.items, ledgerCursor: page.nextCursor ?? null });
      } catch (error) {
        set({ error: BridgeError.from(error).toInfo() });
      } finally {
        set({ busy: false });
      }
    },

    async loadMoreLedger() {
      const cursor = get().ledgerCursor;
      if (!cursor) {
        return;
      }
      set({ busy: true });
      try {
        const page = await bridge.call('account.ledger', { cursor });
        set((s) => ({ ledger: [...s.ledger, ...page.items], ledgerCursor: page.nextCursor ?? null }));
      } catch (error) {
        set({ error: BridgeError.from(error).toInfo() });
      } finally {
        set({ busy: false });
      }
    },

    closeDialog: () => set({ dialog: null }),
    dismissWelcome: () => set({ welcomeDismissed: true }),
  }));

  // The sign-in finishes in the background. Once the host reports success, close the dialog and
  // switch to the account: whoever just signed in wants to generate with it.
  app.subscribe((state, previous) => {
    if (state.account.signedIn && !previous.account.signedIn && store.getState().dialog === 'login') {
      store.setState({ dialog: null });
      if (state.activeBackendId !== CLOUD_BACKEND_ID && state.backends.some((b) => b.id === CLOUD_BACKEND_ID)) {
        void app.getState().selectBackend(CLOUD_BACKEND_ID).catch(() => undefined);
      }
    }
  });

  // Running out of credits leads straight to the redeem dialog.
  for (const source of statusSources) {
    source.subscribe((state, previous) => {
      if (state.status !== previous.status && state.status?.error?.code === 'insufficient_credits') {
        store.getState().openRedeem('redeem.insufficient');
      }
    });
  }

  return store;
}
