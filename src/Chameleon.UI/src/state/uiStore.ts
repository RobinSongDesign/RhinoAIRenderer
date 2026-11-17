import { createStore, type StoreApi } from 'zustand/vanilla';
import type { AssetRef } from '../bridge/types';

export type Mode = 'single' | 'batch';

/** Presentation state shared by several components. */
export interface UiStoreState {
  mode: Mode;
  settingsOpen: boolean;
  preview: AssetRef | null;
  setMode(mode: Mode): void;
  openSettings(): void;
  closeSettings(): void;
  showPreview(asset: AssetRef): void;
  closePreview(): void;
}

export function createUiStore(): StoreApi<UiStoreState> {
  return createStore<UiStoreState>()((set) => ({
    mode: 'single',
    settingsOpen: false,
    preview: null,
    setMode: (mode) => set({ mode }),
    openSettings: () => set({ settingsOpen: true }),
    closeSettings: () => set({ settingsOpen: false }),
    showPreview: (asset) => set({ preview: asset }),
    closePreview: () => set({ preview: null }),
  }));
}
