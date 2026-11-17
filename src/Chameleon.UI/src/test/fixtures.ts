import type { AppState, AssetRef, BackendInfo, Capabilities, JobInfo } from '../bridge/types';

export const capabilities: Capabilities = {
  supportsMask: false,
  maxInputImages: 6,
  imageSizes: ['1K', '2K'],
  aspectRatios: ['1:1', '16:9'],
  maxConcurrency: 2,
};

export const backends: BackendInfo[] = [
  {
    id: 'direct:BltAI',
    kind: 'direct',
    displayName: 'BLT AI',
    ready: true,
    defaultModel: 'model-a',
    models: [
      { id: 'model-a', displayName: 'Model A', capabilities },
      {
        id: 'model-b',
        displayName: 'Model B',
        capabilities: { ...capabilities, imageSizes: [], aspectRatios: [] },
      },
    ],
  },
  { id: 'direct:Gemini', kind: 'direct', displayName: 'Google', ready: false, defaultModel: 'g', models: [] },
];

export function appState(overrides: Partial<AppState> = {}): AppState {
  return {
    protocolVersion: 1,
    appVersion: '2.0.0',
    platform: 'dev',
    rhinoVersion: '',
    logsDirectory: '/data/logs',
    language: 'en',
    backends,
    activeBackendId: 'direct:BltAI',
    activeModelId: 'model-a',
    catalog: {
      styleTemplates: [
        { id: 'custom', name: { zh: '自定义', en: 'Custom' }, prompt: '' },
        { id: 'dusk', name: { zh: '黄昏氛围', en: 'Dusk' }, prompt: 'golden hour' },
      ],
      defaultSystemPrompt: 'system',
      imageSizes: ['0.5K', '1K', '2K', '4K'],
      aspectRatios: ['1:1', '4:3', '16:9'],
      defaultImageSize: '1K',
    },
    settings: { language: 'en', systemPrompt: null, batch: { mode: 'serial', maxConsistencyRefs: 3 } },
    account: { available: false, signedIn: false },
    ...overrides,
  };
}

export function asset(id: string, width = 64, height = 48, mimeType = 'image/png'): AssetRef {
  return { assetId: id, mimeType, width, height, bytes: 100 };
}

export function job(id: string, state: JobInfo['state'], extra: Partial<JobInfo> = {}): JobInfo {
  return { id, state, createdAt: '2026-11-17T00:00:00Z', ...extra };
}
