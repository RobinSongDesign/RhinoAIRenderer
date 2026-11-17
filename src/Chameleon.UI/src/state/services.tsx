import { createContext, useContext, useMemo, type ReactNode } from 'react';
import { useStore, type StoreApi } from 'zustand';
import type { Bridge } from '../bridge/Bridge';
import type { Language } from '../bridge/types';
import { createTranslator, type Translate } from '../i18n';
import { createAppStore, type AppStoreState } from './appStore';
import { createJobsStore, type JobsStoreState } from './jobsStore';
import { createUiStore, type UiStoreState } from './uiStore';

/** Everything components may use. Created once at start-up and handed down through context. */
export interface Services {
  bridge: Bridge;
  app: StoreApi<AppStoreState>;
  jobs: StoreApi<JobsStoreState>;
  ui: StoreApi<UiStoreState>;
}

/** Builds the core services on top of a bridge; feature stores are created next to their features. */
export function createServices(bridge: Bridge, initialLanguage: Language): Services {
  return {
    bridge,
    app: createAppStore(bridge, initialLanguage),
    jobs: createJobsStore(bridge),
    ui: createUiStore(),
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

/** Translator for the current UI language; re-renders the caller when the language changes. */
export function useT(): Translate {
  const language = useAppStore((s) => s.language);
  return useMemo(() => createTranslator(language), [language]);
}
