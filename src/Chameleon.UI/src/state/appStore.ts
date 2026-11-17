import { createStore, type StoreApi } from 'zustand/vanilla';
import type { Bridge } from '../bridge/Bridge';
import type { ConnectionState } from '../bridge/transport';
import type {
  AccountInfo,
  AppState,
  BackendInfo,
  BackendSelection,
  Catalog,
  Language,
  ModelInfo,
  Platform,
  RpcParams,
  UiSettings,
} from '../bridge/types';
import { BridgeError } from '../bridge/BridgeError';
import { CLOUD_BACKEND_ID } from '../bridge/types';
import { normalizeLanguage } from '../i18n';

export interface HostInfo {
  appVersion: string;
  platform: Platform;
  rhinoVersion: string;
  protocolVersion: number;
  logsDirectory: string;
}

export interface AppStoreState {
  loadState: 'loading' | 'ready' | 'failed';
  loadError: string | null;
  connection: ConnectionState;
  host: HostInfo | null;
  language: Language;
  backends: BackendInfo[];
  activeBackendId: string;
  activeModelId: string;
  catalog: Catalog | null;
  settings: UiSettings | null;
  account: AccountInfo;

  /** Fetches the full state from the host. Also used to resynchronize after a reconnect. */
  load(): Promise<void>;
  /** Makes a backend (and optionally a model) the active one. */
  selectBackend(backendId: string, modelId?: string): Promise<void>;
  /** Changes user settings; the host echoes the result. */
  updateSettings(patch: RpcParams<'settings.update'>): Promise<void>;
}

/** Creates the application store and keeps it in sync with host events. */
export function createAppStore(bridge: Bridge, initialLanguage: Language): StoreApi<AppStoreState> {
  const store = createStore<AppStoreState>()((set, get) => ({
    loadState: 'loading',
    loadError: null,
    connection: 'connecting',
    host: null,
    language: initialLanguage,
    backends: [],
    activeBackendId: '',
    activeModelId: '',
    catalog: null,
    settings: null,
    account: { available: false, signedIn: false },

    async load() {
      try {
        applyState(await bridge.call('app.getState'));
      } catch (error) {
        if (get().loadState !== 'ready') {
          set({ loadState: 'failed', loadError: BridgeError.from(error).message });
        }
      }
    },

    async selectBackend(backendId, modelId) {
      // Optimistic, so the selects do not jump back while the host answers.
      set({ activeBackendId: backendId, ...(modelId ? { activeModelId: modelId } : {}) });
      try {
        applySelection(await bridge.call('backend.select', { backendId, modelId }));
      } catch (error) {
        // Do not leave the optimistic choice on screen: show what the host actually has.
        void get().load();
        throw error;
      }
    },

    async updateSettings(patch) {
      applySettings(await bridge.call('settings.update', patch));
    },
  }));

  /** Backend the current catalog was loaded for (the active one may have changed since). */
  let catalogBackendId = '';
  let catalogRequest = 0;

  function applyState(state: AppState): void {
    catalogBackendId = state.activeBackendId;
    store.setState({
      loadState: 'ready',
      loadError: null,
      host: {
        appVersion: state.appVersion,
        platform: state.platform,
        rhinoVersion: state.rhinoVersion,
        protocolVersion: state.protocolVersion,
        logsDirectory: state.logsDirectory,
      },
      language: normalizeLanguage(state.language),
      backends: state.backends,
      activeBackendId: state.activeBackendId,
      activeModelId: state.activeModelId,
      catalog: state.catalog,
      settings: state.settings,
      account: state.account ?? { available: false, signedIn: false },
    });
  }

  function applySelection(selection: BackendSelection): void {
    store.setState({
      backends: selection.backends,
      activeBackendId: selection.activeBackendId,
      activeModelId: selection.activeModelId,
    });
    if (isCloud(catalogBackendId) !== isCloud(selection.activeBackendId)) {
      void refreshCatalog();
    }
  }

  function applyAccount(account: AccountInfo): void {
    const wasSignedIn = store.getState().account.signedIn;
    store.setState({ account });
    // The gateway's catalog becomes known (or forgotten) with the sign-in.
    if (wasSignedIn !== account.signedIn && isCloud(store.getState().activeBackendId)) {
      void refreshCatalog();
    }
  }

  /**
   * The catalog depends on the active backend: account mode uses the gateway's templates
   * (bridge-protocol.md §4.7). Only app.getState carries it, so fetch it again when that changes.
   */
  async function refreshCatalog(): Promise<void> {
    // Refreshes can overlap (sign-in and backend switch); only the latest one reflects the host.
    const request = ++catalogRequest;
    try {
      const state = await bridge.call('app.getState');
      if (request !== catalogRequest) {
        return;
      }
      catalogBackendId = state.activeBackendId;
      store.setState({ catalog: state.catalog });
    } catch {
      // Keep the current catalog; the next full load resynchronizes.
    }
  }

  function applySettings(settings: UiSettings): void {
    store.setState({ settings, language: normalizeLanguage(settings.language) });
  }

  bridge.on('backends.changed', applySelection);
  bridge.on('settings.changed', applySettings);
  bridge.on('account.changed', applyAccount);
  bridge.onConnectionChange((connection) => {
    const wasDisconnected = store.getState().connection === 'disconnected';
    store.setState({ connection });
    // Events sent while the stream was down are lost: start again from a full snapshot.
    if (connection === 'connected' && wasDisconnected) {
      void store.getState().load();
    }
  });

  return store;
}

/** Whether a backend id is the account-mode backend. */
function isCloud(backendId: string): boolean {
  return backendId === CLOUD_BACKEND_ID;
}

/** The active backend, if it still exists. */
export function selectActiveBackend(state: AppStoreState): BackendInfo | undefined {
  return state.backends.find((b) => b.id === state.activeBackendId);
}

/** The active model of the active backend. */
export function selectActiveModel(state: AppStoreState): ModelInfo | undefined {
  return selectActiveBackend(state)?.models.find((m) => m.id === state.activeModelId);
}

/** Image size tiers the active model accepts (the full catalog list when it does not say). */
export function selectImageSizes(state: AppStoreState): string[] {
  const sizes = selectActiveModel(state)?.capabilities.imageSizes;
  return sizes && sizes.length > 0 ? sizes : (state.catalog?.imageSizes ?? []);
}

/** Aspect ratios the active model accepts (the full catalog list when it does not say). */
export function selectAspectRatios(state: AppStoreState): string[] {
  const ratios = selectActiveModel(state)?.capabilities.aspectRatios;
  return ratios && ratios.length > 0 ? ratios : (state.catalog?.aspectRatios ?? []);
}
