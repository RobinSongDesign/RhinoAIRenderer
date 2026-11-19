import { describe, expect, it, vi } from 'vitest';
import { Bridge } from '../../bridge/Bridge';
import { BridgeError } from '../../bridge/BridgeError';
import type { ProviderDto } from '../../bridge/types';
import { FakeTransport } from '../../test/fakeTransport';
import { accountPageUrl, createRelaySignInStore } from './relaySignInStore';

const relay: ProviderDto = {
  id: 'Relay',
  displayName: 'Relay',
  baseUrl: 'https://relay.example.com/',
  apiFormat: 'gemini',
  authType: 'bearer',
  models: ['m'],
  defaultModel: 'm',
  isBuiltIn: true,
  isOverridden: false,
  enableSearchGrounding: true,
  hasKey: false,
  supportsSignIn: true,
};

function setup() {
  const transport = new FakeTransport()
    .handle('providers.list', () => [relay, { ...relay, id: 'Plain', supportsSignIn: false }])
    .handle('providers.signIn', (p) => {
      const { username, password } = p as { username: string; password: string };
      if (password !== 'test-password-123') {
        throw new BridgeError('auth_failed', 'wrong password');
      }
      return username === 'bob' ? { status: 'needsTwoFactor', flowToken: 'flow-1' } : { status: 'signedIn', username };
    })
    .handle('providers.signInVerify', () => {
      return { status: 'signedIn', username: 'bob' };
    })
    .handle('providers.signOut', () => null)
    .handle('shell.openUrl', () => null);
  const onChanged = vi.fn();
  const store = createRelaySignInStore({ bridge: new Bridge(transport), onChanged });
  // Every call except the provider lookups, in order.
  const calls = () => transport.calls.filter((c) => c.method !== 'providers.list');
  return { store, calls, onChanged };
}

describe('accountPageUrl', () => {
  it('appends the page to the site root, whatever the trailing slashes', () => {
    expect(accountPageUrl('https://relay.example.com/', 'register')).toBe('https://relay.example.com/sign-up');
    expect(accountPageUrl('https://relay.example.com', 'forgotPassword')).toBe('https://relay.example.com/forgot-password');
  });
});

describe('relay sign-in store', () => {
  it('opens only for providers that support signing in', async () => {
    const { store } = setup();
    await store.getState().open('Plain');
    expect(store.getState().target).toBeNull();

    await store.getState().open('Relay');
    expect(store.getState().target).toEqual({ providerId: 'Relay', displayName: 'Relay', baseUrl: 'https://relay.example.com/' });
  });

  it('signs in, closes the dialog and reports the change', async () => {
    const { store, calls, onChanged } = setup();
    await store.getState().open('Relay');

    const done = await store.getState().signIn(' alice ', 'test-password-123');

    expect(done).toBe(true);
    expect(calls()[0]).toEqual({ method: 'providers.signIn', params: { id: 'Relay', username: 'alice', password: 'test-password-123' } });
    expect(store.getState().target).toBeNull();
    expect(onChanged).toHaveBeenCalledTimes(1);
    // The password never becomes state.
    expect(JSON.stringify(store.getState())).not.toContain('test-password-123');
  });

  it('keeps the dialog open with the error after a refusal', async () => {
    const { store, onChanged } = setup();
    await store.getState().open('Relay');

    expect(await store.getState().signIn('alice', 'nope')).toBe(false);

    expect(store.getState().target).not.toBeNull();
    expect(store.getState().error).toMatchObject({ code: 'auth_failed', message: 'wrong password' });
    expect(store.getState().busy).toBe(false);
    expect(onChanged).not.toHaveBeenCalled();
  });

  it('asks for a code and sends it with the flow token', async () => {
    const { store, calls, onChanged } = setup();
    await store.getState().open('Relay');

    expect(await store.getState().signIn('bob', 'test-password-123')).toBe(false);
    expect(store.getState().step).toBe('twoFactor');
    expect(store.getState().error).toBeNull();

    expect(await store.getState().verify(' 654321 ')).toBe(true);
    expect(calls()[1]).toEqual({ method: 'providers.signInVerify', params: { id: 'Relay', flowToken: 'flow-1', code: '654321' } });
    expect(store.getState().target).toBeNull();
    expect(onChanged).toHaveBeenCalledTimes(1);
  });

  it('does not send empty credentials or codes', async () => {
    const { store, calls } = setup();
    await store.getState().open('Relay');

    expect(await store.getState().signIn('  ', 'x')).toBe(false);
    expect(await store.getState().signIn('alice', '')).toBe(false);
    expect(await store.getState().verify('123456')).toBe(false);
    expect(calls()).toEqual([]);
  });

  it('signs out and opens the account pages of the provider', async () => {
    const { store, calls, onChanged } = setup();
    await store.getState().signOut('Relay');
    await store.getState().open('Relay');
    await store.getState().openPage('register');

    expect(calls()).toEqual([
      { method: 'providers.signOut', params: { id: 'Relay' } },
      { method: 'shell.openUrl', params: { url: 'https://relay.example.com/sign-up' } },
    ]);
    expect(onChanged).toHaveBeenCalledTimes(1);
  });
});
