import type { ApiFormat, AuthType, ProviderDto } from '../../bridge/types';
import type { MessageKey } from '../../i18n';

/** Editable copy of a provider in the add/edit form. Models are edited as one comma-separated line. */
export interface ProviderDraft {
  id: string;
  isBuiltIn: boolean;
  displayName: string;
  baseUrl: string;
  apiFormat: ApiFormat;
  authType: AuthType;
  models: string;
  enableSearchGrounding: boolean;
  /** The provider reports its balance like One API / new-api relays do. */
  reportsBalance: boolean;
  topUpUrl: string;
}

/** Defaults filled in when the OpenAI format is chosen on an empty form (as the WPF dialog did). */
export const OPENAI_DEFAULT_URL = 'https://api.openai.com';
export const OPENAI_DEFAULT_MODEL = 'gpt-image-2';

export function emptyDraft(): ProviderDraft {
  return {
    id: '',
    isBuiltIn: false,
    displayName: '',
    baseUrl: '',
    apiFormat: 'gemini',
    authType: 'bearer',
    models: '',
    enableSearchGrounding: true,
    reportsBalance: false,
    topUpUrl: '',
  };
}

export function draftFrom(provider: ProviderDto): ProviderDraft {
  return {
    id: provider.id,
    isBuiltIn: provider.isBuiltIn,
    displayName: provider.displayName,
    baseUrl: provider.baseUrl,
    apiFormat: provider.apiFormat,
    authType: provider.authType,
    // Default model first, so it stays the default after saving.
    models: [provider.defaultModel, ...provider.models.filter((m) => m !== provider.defaultModel)]
      .filter(Boolean)
      .join(', '),
    enableSearchGrounding: provider.enableSearchGrounding,
    reportsBalance: provider.balanceApi === 'oneapi',
    topUpUrl: provider.topUpUrl ?? '',
  };
}

/** Applies a change of API format: OpenAI only knows bearer auth and gets defaults on an empty form. */
export function withApiFormat(draft: ProviderDraft, apiFormat: ApiFormat): ProviderDraft {
  if (apiFormat !== 'openai') {
    return { ...draft, apiFormat };
  }
  return {
    ...draft,
    apiFormat,
    authType: 'bearer',
    baseUrl: draft.baseUrl.trim() ? draft.baseUrl : OPENAI_DEFAULT_URL,
    models: draft.models.trim() ? draft.models : OPENAI_DEFAULT_MODEL,
  };
}

/** Splits the models line into unique, trimmed ids. */
export function parseModels(text: string): string[] {
  const seen = new Set<string>();
  for (const part of text.split(/[,\n，]/)) {
    const id = part.trim();
    if (id) {
      seen.add(id);
    }
  }
  return [...seen];
}

/** First problem with the draft, or null when it can be saved. */
export function validateDraft(draft: ProviderDraft): MessageKey | null {
  if (!draft.displayName.trim()) {
    return 'settings.validation.name';
  }
  if (!/^https?:\/\/\S+$/i.test(draft.baseUrl.trim())) {
    return 'settings.validation.url';
  }
  if (parseModels(draft.models).length === 0) {
    return 'settings.validation.models';
  }
  // The page opens in the system browser, which only ever gets https addresses.
  if (draft.topUpUrl.trim() && !/^https:\/\/\S+$/i.test(draft.topUpUrl.trim())) {
    return 'settings.validation.topUpUrl';
  }
  return null;
}
