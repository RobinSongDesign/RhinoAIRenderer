import { createStore, type StoreApi } from 'zustand/vanilla';
import type { Bridge } from '../bridge/Bridge';
import type { ConnectionState } from '../bridge/transport';
import type {
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
import { normalizeLanguage } from '../i18n';

export interface HostInfo {
  appVersion: string;
  platform: Platform;
  rhinoVersion: string;
  protocolVersion: number;
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
      applySelection(await bridge.call('backend.select', { backendId, modelId }));
    },

    async updateSettings(patch) {
      applySettings(await bridge.call('settings.update', patch));
    },
  }));

  function applyState(state: AppState): void {
    store.setState({
      loadState: 'ready',
      loadError: null,
      host: {
        appVersion: state.appVersion,
        platform: state.platform,
        rhinoVersion: state.rhinoVersion,
        protocolVersion: state.protocolVersion,
      },
      language: normalizeLanguage(state.language),
      backends: state.backends,
      activeBackendId: state.activeBackendId,
      activeModelId: state.activeModelId,
      catalog: state.catalog,
      settings: state.settings,
    });
  }

  function applySelection(selection: BackendSelection): void {
    store.setState({
      backends: selection.backends,
      activeBackendId: selection.activeBackendId,
      activeModelId: selection.activeModelId,
    });
  }

  function applySettings(settings: UiSettings): void {
    store.setState({ settings, language: normalizeLanguage(settings.language) });
  }

  bridge.on('backends.changed', applySelection);
  bridge.on('settings.changed', applySettings);
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
