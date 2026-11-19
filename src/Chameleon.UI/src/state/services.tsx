import { createContext, useContext, useMemo, type ReactNode } from 'react';
import { useStore, type StoreApi } from 'zustand';
import type { Bridge } from '../bridge/Bridge';
import type { Language } from '../bridge/types';
import { createTranslator, type Translate } from '../i18n';
import { createAppStore, type AppStoreState } from './appStore';
import { createJobsStore, type JobsStoreState } from './jobsStore';
import { createUiStore, type UiStoreState } from './uiStore';
import { createParamsStore, type ParamsStoreState } from '../features/params/paramsStore';
import { createSingleStore, type SingleStoreState } from '../features/single/singleStore';
import { createSessionStore, type SessionStoreState } from '../features/history/sessionStore';
import { createBatchStore, type BatchStoreState } from '../features/batch/batchStore';
import { createSettingsStore, type SettingsStoreState } from '../features/settings/settingsStore';
import { createAccountStore, type AccountStoreState } from '../features/account/accountStore';
import { createRelaySignInStore, type RelaySignInStoreState } from '../features/relaySignIn/relaySignInStore';
import { BitmapCache } from '../features/canvas/bitmapCache';
import { decodeImage, type CanvasBitmap } from '../features/canvas/decode';

/** Everything components may use. Created once at start-up and handed down through context. */
export interface Services {
  bridge: Bridge;
  app: StoreApi<AppStoreState>;
  jobs: StoreApi<JobsStoreState>;
  ui: StoreApi<UiStoreState>;
  params: StoreApi<ParamsStoreState>;
  single: StoreApi<SingleStoreState>;
  session: StoreApi<SessionStoreState>;
  batch: StoreApi<BatchStoreState>;
  settings: StoreApi<SettingsStoreState>;
  account: StoreApi<AccountStoreState>;
  relaySignIn: StoreApi<RelaySignInStoreState>;
  /** Decoded images shared by every canvas. */
  bitmaps: BitmapCache<CanvasBitmap>;
}

/** Builds every store on top of a bridge. Feature stores live next to their features. */
export function createServices(bridge: Bridge, initialLanguage: Language): Services {
  const app = createAppStore(bridge, initialLanguage);
  const jobs = createJobsStore(bridge);
  const params = createParamsStore(bridge);
  const session = createSessionStore(bridge, app);
  const single = createSingleStore({ bridge, app, jobs, params, session });
  const batch = createBatchStore({ bridge, app, jobs, params });
  const settings = createSettingsStore(bridge);
  return {
    bridge,
    app,
    jobs,
    ui: createUiStore(),
    params,
    single,
    session,
    batch,
    settings,
    account: createAccountStore({ bridge, app, statusSources: [single, batch] }),
    // The backend list follows by itself ("backends.changed"); the settings dialog caches providers.
    relaySignIn: createRelaySignInStore({ bridge, onChanged: () => void settings.getState().refresh() }),
    bitmaps: new BitmapCache<CanvasBitmap>((assetId) => decodeImage(bridge.assetUrl(assetId))),
  };
}

const ServicesContext = createContext<Services | null>(null);

export function ServicesProvider({ services, children }: { services: Services; children: ReactNode }) {
  return <ServicesContext.Provider value={services}>{children}</ServicesContext.Provider>;
}

export function useServices(): Services {
  const services = useContext(ServicesContext);
  if (!services) {
    throw new Error('useServices must be used inside <ServicesProvider>.');
  }
  return services;
}

export function useBridge(): Bridge {
  return useServices().bridge;
}

export function useAppStore<T>(selector: (state: AppStoreState) => T): T {
  return useStore(useServices().app, selector);
}

export function useJobsStore<T>(selector: (state: JobsStoreState) => T): T {
  return useStore(useServices().jobs, selector);
}

export function useUiStore<T>(selector: (state: UiStoreState) => T): T {
  return useStore(useServices().ui, selector);
}

export function useParamsStore<T>(selector: (state: ParamsStoreState) => T): T {
  return useStore(useServices().params, selector);
}

export function useSingleStore<T>(selector: (state: SingleStoreState) => T): T {
  return useStore(useServices().single, selector);
}

export function useBatchStore<T>(selector: (state: BatchStoreState) => T): T {
  return useStore(useServices().batch, selector);
}

export function useSettingsStore<T>(selector: (state: SettingsStoreState) => T): T {
  return useStore(useServices().settings, selector);
}

export function useAccountStore<T>(selector: (state: AccountStoreState) => T): T {
  return useStore(useServices().account, selector);
}

export function useRelaySignInStore<T>(selector: (state: RelaySignInStoreState) => T): T {
  return useStore(useServices().relaySignIn, selector);
}

export function useSessionStore<T>(selector: (state: SessionStoreState) => T): T {
  return useStore(useServices().session, selector);
}

/** Translator for the current UI language; re-renders the caller when the language changes. */
export function useT(): Translate {
  const language = useAppStore((s) => s.language);
  return useMemo(() => createTranslator(language), [language]);
}
