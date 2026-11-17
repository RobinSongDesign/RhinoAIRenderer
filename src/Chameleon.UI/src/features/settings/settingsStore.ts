import { createStore, type StoreApi } from 'zustand/vanilla';
import type { Bridge } from '../../bridge/Bridge';
import type { ProviderDto } from '../../bridge/types';
import { status, type StatusMessage } from '../../state/status';
import {
  draftFrom,
  emptyDraft,
  parseModels,
  validateDraft,
  withApiFormat,
  type ProviderDraft,
} from './providerDraft';

export type KeyTestState = 'idle' | 'testing' | 'ok' | 'failed';

export interface SettingsStoreState {
  providers: ProviderDto[];
  selectedId: string;
  /** Form being edited; null shows the provider details. */
  draft: ProviderDraft | null;
  /** Typed key; never pre-filled, since the host never returns stored keys. */
  keyInput: string;
  testModel: string;
  testState: KeyTestState;
  confirmingDelete: boolean;
  busy: boolean;
  message: StatusMessage | null;

  open(preferredId?: string): Promise<void>;
  select(id: string): void;
  startAdd(): void;
  startEdit(): void;
  updateDraft(patch: Partial<ProviderDraft>): void;
  cancelEdit(): void;
  saveDraft(): Promise<void>;
  askDelete(): void;
  cancelDelete(): void;
  deleteSelected(): Promise<void>;
  resetSelected(): Promise<void>;
  setKeyInput(key: string): void;
  saveKey(): Promise<void>;
  clearKey(): Promise<void>;
  setTestModel(modelId: string): void;
  testKey(): Promise<void>;
  openKeyPage(): Promise<void>;
}

/** State of the settings dialog's provider section. */
export function createSettingsStore(bridge: Bridge): StoreApi<SettingsStoreState> {
  const store = createStore<SettingsStoreState>()((set, get) => ({
    providers: [],
    selectedId: '',
    draft: null,
    keyInput: '',
    testModel: '',
    testState: 'idle',
    confirmingDelete: false,
    busy: false,
    message: null,

    async open(preferredId) {
      set({ draft: null, keyInput: '', testState: 'idle', confirmingDelete: false, message: null });
      await reload(preferredId ?? get().selectedId);
    },

    select(id) {
      const provider = get().providers.find((p) => p.id === id);
      set({
        selectedId: id,
        draft: null,
        keyInput: '',
        testModel: provider?.defaultModel ?? '',
        testState: 'idle',
        confirmingDelete: false,
        message: null,
      });
    },

    startAdd: () => set({ draft: emptyDraft(), message: null, confirmingDelete: false }),

    startEdit() {
      const provider = selected();
      if (provider) {
        set({ draft: draftFrom(provider), message: null, confirmingDelete: false });
      }
    },

    updateDraft(patch) {
      const draft = get().draft;
      if (!draft) {
        return;
      }
      // A format change also adjusts other fields, so it is applied before the rest of the patch.
      const formatted =
        patch.apiFormat && patch.apiFormat !== draft.apiFormat
          ? withApiFormat(draft, patch.apiFormat)
          : draft;
      set({ draft: { ...formatted, ...patch, apiFormat: formatted.apiFormat } });
    },

    cancelEdit: () => set({ draft: null, message: null }),

    async saveDraft() {
      const draft = get().draft;
      if (!draft) {
        return;
      }
      const problem = validateDraft(draft);
      if (problem) {
        set({ message: status.info(problem) });
        return;
      }
      const models = parseModels(draft.models);
      await run(async () => {
        const saved = await bridge.call('providers.save', {
          id: draft.id || undefined,
          displayName: draft.displayName.trim(),
          baseUrl: draft.baseUrl.trim(),
          apiFormat: draft.apiFormat,
          authType: draft.authType,
          models,
          defaultModel: models[0],
          enableSearchGrounding: draft.enableSearchGrounding,
        });
        set({ draft: null });
        await reload(saved.id);
        set({ message: status.success('settings.saved') });
      });
    },

    askDelete: () => set({ confirmingDelete: true }),
    cancelDelete: () => set({ confirmingDelete: false }),

    async deleteSelected() {
      const provider = selected();
      if (!provider || provider.isBuiltIn) {
        return;
      }
      await run(async () => {
        await bridge.call('providers.delete', { id: provider.id });
        set({ confirmingDelete: false });
        await reload();
      });
    },

    async resetSelected() {
      const provider = selected();
      if (!provider?.isBuiltIn) {
        return;
      }
      await run(async () => {
        await bridge.call('providers.resetBuiltIn', { id: provider.id });
        await reload(provider.id);
        set({ message: status.success('settings.saved') });
      });
    },

    setKeyInput: (keyInput) => set({ keyInput, testState: 'idle' }),

    async saveKey() {
      const provider = selected();
      const key = get().keyInput.trim();
      if (!provider || !key) {
        set({ message: status.info('settings.keyMissing') });
        return;
      }
      await run(async () => {
        await bridge.call('providers.setKey', { id: provider.id, key });
        set({ keyInput: '' });
        await reload(provider.id);
        set({ message: status.success('settings.keyStored') });
      });
    },

    async clearKey() {
      const provider = selected();
      if (!provider) {
        return;
      }
      await run(async () => {
        await bridge.call('providers.setKey', { id: provider.id, key: '' });
        await reload(provider.id);
        set({ message: status.info('settings.keyCleared') });
      });
    },

    setTestModel: (testModel) => set({ testModel, testState: 'idle' }),

    async testKey() {
      const provider = selected();
      const key = get().keyInput.trim();
      if (!provider || (!key && !provider.hasKey)) {
        set({ message: status.info('settings.keyMissing') });
        return;
      }
      set({ testState: 'testing', message: status.busy('settings.testing') });
      try {
        await bridge.call('providers.testKey', {
          id: provider.id,
          key: key || undefined,
          modelId: get().testModel || undefined,
        });
        set({ testState: 'ok', message: status.success('settings.keyValid') });
      } catch (error) {
        set({ testState: 'failed', message: status.error(error) });
      }
    },

    async openKeyPage() {
      const url = selected()?.apiKeyUrl;
      if (url) {
        await bridge
          .call('shell.openUrl', { url })
          .catch((error: unknown) => set({ message: status.error(error) }));
      }
    },
  }));

  function selected(): ProviderDto | undefined {
    const state = store.getState();
    return state.providers.find((p) => p.id === state.selectedId);
  }

  async function reload(preferredId?: string): Promise<void> {
    const providers = await bridge.call('providers.list');
    const keep = providers.find((p) => p.id === preferredId) ?? providers[0];
    const testModelStillValid = keep && keep.models.includes(store.getState().testModel);
    store.setState({
      providers,
      selectedId: keep?.id ?? '',
      testModel: testModelStillValid ? store.getState().testModel : (keep?.defaultModel ?? ''),
    });
  }

  async function run(work: () => Promise<void>): Promise<void> {
    store.setState({ busy: true, message: null });
    try {
      await work();
    } catch (error) {
      store.setState({ message: status.error(error) });
    } finally {
      store.setState({ busy: false });
    }
  }

  return store;
}
