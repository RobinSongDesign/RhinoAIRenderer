import { describe, expect, it } from 'vitest';
import { Bridge } from '../../bridge/Bridge';
import { BridgeError } from '../../bridge/BridgeError';
import type { ProviderDto } from '../../bridge/types';
import { FakeTransport } from '../../test/fakeTransport';
import { draftFrom, emptyDraft, parseModels, validateDraft, withApiFormat } from './providerDraft';
import { createSettingsStore } from './settingsStore';

function provider(overrides: Partial<ProviderDto> = {}): ProviderDto {
  return {
    id: 'BltAI',
    displayName: 'BLT AI',
    baseUrl: 'https://hk-api.gptbest.vip',
    apiFormat: 'gemini',
    authType: 'bearer',
    models: ['m1', 'm2'],
    defaultModel: 'm2',
    apiKeyUrl: 'https://example.com/key',
    isBuiltIn: true,
    isOverridden: false,
    enableSearchGrounding: true,
    hasKey: false,
    ...overrides,
  };
}

describe('provider drafts', () => {
  it('parse models from commas, full-width commas and new lines, without duplicates', () => {
    expect(parseModels(' a, b，c\nd ,a,, ')).toEqual(['a', 'b', 'c', 'd']);
  });

  it('put the default model first so it stays the default', () => {
    expect(draftFrom(provider()).models).toBe('m2, m1');
  });

  it('fill OpenAI defaults on an empty form and lock bearer auth, but keep what was typed', () => {
    const filled = withApiFormat({ ...emptyDraft(), authType: 'goog' }, 'openai');
    expect(filled).toMatchObject({
      baseUrl: 'https://api.openai.com',
      models: 'gpt-image-2',
      authType: 'bearer',
    });

    const kept = withApiFormat(
      { ...emptyDraft(), baseUrl: 'https://relay.example.com', models: 'x' },
      'openai',
    );
    expect(kept).toMatchObject({ baseUrl: 'https://relay.example.com', models: 'x' });
  });

  it('read and validate the balance and top-up options', () => {
    const relay = draftFrom(provider({ isBuiltIn: false, balanceApi: 'oneapi', topUpUrl: 'https://relay.example.com/pay' }));
    expect(relay).toMatchObject({ reportsBalance: true, topUpUrl: 'https://relay.example.com/pay' });
    expect(draftFrom(provider())).toMatchObject({ reportsBalance: false, topUpUrl: '' });

    const ok = { ...emptyDraft(), displayName: 'Relay', baseUrl: 'https://relay.example.com', models: 'm' };
    expect(validateDraft({ ...ok, topUpUrl: 'https://relay.example.com/pay' })).toBeNull();
    expect(validateDraft({ ...ok, topUpUrl: 'http://relay.example.com/pay' })).toBe('settings.validation.topUpUrl');
  });

  it('validate name, URL and models', () => {
    const ok = { ...emptyDraft(), displayName: 'Relay', baseUrl: 'https://relay.example.com', models: 'm' };
    expect(validateDraft(ok)).toBeNull();
    expect(validateDraft({ ...ok, displayName: ' ' })).toBe('settings.validation.name');
    expect(validateDraft({ ...ok, baseUrl: 'ftp://x' })).toBe('settings.validation.url');
    expect(validateDraft({ ...ok, models: ' , ' })).toBe('settings.validation.models');
  });
});

describe('settingsStore', () => {
  function setup(
    providers: ProviderDto[] = [
      provider(),
      provider({ id: 'relay', isBuiltIn: false, displayName: 'Relay' }),
    ],
  ) {
    let current = providers;
    const transport = new FakeTransport()
      .handle('providers.list', () => current)
      .handle('providers.save', (p) => {
        const saved = provider({
          ...(p as Partial<ProviderDto>),
          id: (p as ProviderDto).id || 'new1',
          isBuiltIn: false,
        });
        current = [...current.filter((c) => c.id !== saved.id), saved];
        return saved;
      })
      .handle('providers.delete', (p) => {
        current = current.filter((c) => c.id !== (p as { id: string }).id);
        return null;
      })
      .handle('providers.setKey', (p) => {
        const { id, key } = p as { id: string; key: string };
        current = current.map((c) => (c.id === id ? { ...c, hasKey: key !== '' } : c));
        return { hasKey: key !== '' };
      })
      .handle('providers.testKey', (p) => {
        if ((p as { key?: string }).key === 'bad') {
          throw new BridgeError('auth_failed', 'rejected', 'invalid key');
        }
        return { ok: true };
      })
      .handle('shell.openUrl', () => null);
    return { transport, store: createSettingsStore(new Bridge(transport)) };
  }

  it('opens on the preferred provider with its default model for testing', async () => {
    const { store } = setup();

    await store.getState().open('BltAI');

    expect(store.getState().selectedId).toBe('BltAI');
    expect(store.getState().testModel).toBe('m2');
  });

  it('saves a new provider from the form and selects it', async () => {
    const { store, transport } = setup();
    await store.getState().open();
    store.getState().startAdd();
    store
      .getState()
      .updateDraft({ displayName: 'Mine', baseUrl: 'https://mine.example.com', models: 'a, b' });

    await store.getState().saveDraft();

    expect(transport.callsOf('providers.save')[0]).toMatchObject({
      displayName: 'Mine',
      models: ['a', 'b'],
      defaultModel: 'a',
    });
    expect(store.getState().draft).toBeNull();
    expect(store.getState().selectedId).toBe('new1');
    expect(store.getState().message?.key).toBe('settings.saved');
  });

  it('does not send an invalid form', async () => {
    const { store, transport } = setup();
    await store.getState().open();
    store.getState().startAdd();

    await store.getState().saveDraft();

    expect(store.getState().message?.key).toBe('settings.validation.name');
    expect(transport.callsOf('providers.save')).toHaveLength(0);
  });

  it('deletes a user provider only after confirmation', async () => {
    const { store, transport } = setup();
    await store.getState().open('relay');

    store.getState().askDelete();
    expect(store.getState().confirmingDelete).toBe(true);
    await store.getState().deleteSelected();

    expect(transport.callsOf('providers.delete')).toEqual([{ id: 'relay' }]);
    expect(store.getState().providers.map((p) => p.id)).toEqual(['BltAI']);
    expect(store.getState().selectedId).toBe('BltAI');
  });

  it('stores a key, never keeps it in the form, and can clear it', async () => {
    const { store, transport } = setup();
    await store.getState().open('BltAI');

    store.getState().setKeyInput('  test-key-123 ');
    await store.getState().saveKey();
    expect(transport.callsOf('providers.setKey')[0]).toEqual({ id: 'BltAI', key: 'test-key-123' });
    expect(store.getState().keyInput).toBe('');
    expect(store.getState().providers[0].hasKey).toBe(true);

    await store.getState().clearKey();
    expect(store.getState().providers[0].hasKey).toBe(false);
  });

  it('tests the typed key, or the stored one, and reports failures by code', async () => {
    const { store, transport } = setup([provider({ hasKey: true })]);
    await store.getState().open();

    await store.getState().testKey();
    expect(store.getState().testState).toBe('ok');
    expect(transport.callsOf('providers.testKey')[0]).toEqual({ id: 'BltAI', key: undefined, modelId: 'm2' });

    store.getState().setKeyInput('bad');
    await store.getState().testKey();
    expect(store.getState().testState).toBe('failed');
    expect(store.getState().message).toMatchObject({
      key: 'error.auth_failed',
      error: { detail: 'invalid key' },
    });
  });

  it('asks for a key before testing a provider that has none', async () => {
    const { store, transport } = setup();
    await store.getState().open('BltAI');

    await store.getState().testKey();

    expect(store.getState().message?.key).toBe('settings.keyMissing');
    expect(transport.callsOf('providers.testKey')).toHaveLength(0);
  });

  it('opens the provider key page through the host', async () => {
    const { store, transport } = setup();
    await store.getState().open('BltAI');

    await store.getState().openKeyPage();

    expect(transport.callsOf('shell.openUrl')).toEqual([{ url: 'https://example.com/key' }]);
  });
});
