import { describe, expect, it } from 'vitest';
import { createStore } from 'zustand/vanilla';
import { Bridge } from '../../bridge/Bridge';
import { BridgeError } from '../../bridge/BridgeError';
import type { AccountInfo, BackendInfo } from '../../bridge/types';
import { createAppStore } from '../../state/appStore';
import type { StatusMessage } from '../../state/status';
import { FakeTransport, flush } from '../../test/fakeTransport';
import { appState, backends } from '../../test/fixtures';
import { createAccountStore, formatRedeemCode } from './accountStore';

const cloud: BackendInfo = { id: 'cloud', kind: 'cloud', displayName: 'Account', ready: false, defaultModel: 'cloud-standard', models: [] };
const signedIn: AccountInfo = { available: true, signedIn: true, email: 'dev@example.com', credits: 10 };

function setup() {
  const transport = new FakeTransport()
    .handle('auth.startLogin', () => ({ state: 'pending', userCode: 'ABCD-EFGH', verificationUrl: 'https://example.com/device' }))
    .handle('auth.cancelLogin', () => null)
    .handle('auth.logout', () => null)
    .handle('account.redeem', (p) => {
      const code = (p as { code: string }).code;
      if (code === 'USED-USED-USED-USED') {
        throw new BridgeError('conflict', 'used');
      }
      if (code === 'GONE-GONE-GONE-GONE') {
        throw new BridgeError('gone', 'expired');
      }
      if (code === 'NONE-NONE-NONE-NONE') {
        throw new BridgeError('not_found', 'unknown');
      }
      return { creditsAdded: 100, credits: 110 };
    })
    .handle('account.ledger', (p) => {
      const cursor = (p as { cursor?: string } | undefined)?.cursor;
      const item = (id: string) => ({ id, type: 'redeem', delta: 100, balanceAfter: 110, createdAt: '2026-01-01T00:00:00Z' });
      return cursor ? { items: [item('l2')] } : { items: [item('l1')], nextCursor: 'c1' };
    });
  const bridge = new Bridge(transport);
  const app = createAppStore(bridge, 'en');
  const single = createStore<{ status: StatusMessage | null }>()(() => ({ status: null }));
  const store = createAccountStore({ bridge, app, statusSources: [single] });
  return { transport, app, single, store };
}

describe('formatRedeemCode', () => {
  it('groups what was typed into blocks of four, upper case, without stray characters', () => {
    expect(formatRedeemCode('abcd')).toBe('ABCD');
    expect(formatRedeemCode('abcde')).toBe('ABCD-E');
    expect(formatRedeemCode('test test-test_test')).toBe('TEST-TEST-TEST-TEST');
    expect(formatRedeemCode('ABCD-EFGH-IJKL-MNOP-QRST')).toBe('ABCD-EFGH-IJKL-MNOP');
  });
});

describe('accountStore', () => {
  it('opens the login dialog and closes it once the host reports the sign-in', async () => {
    const { app, store, transport } = setup();

    await store.getState().startLogin();
    expect(store.getState().dialog).toBe('login');
    expect(transport.callsOf('auth.startLogin')).toHaveLength(1);

    transport.emit('account.changed', signedIn);
    expect(app.getState().account.signedIn).toBe(true);
    expect(store.getState().dialog).toBeNull();
  });

  it('switches to the account backend after signing in', async () => {
    const { app, store, transport } = setup();
    transport.handle('app.getState', () => appState({ account: { available: true, signedIn: false }, backends: [...backends, cloud] }));
    transport.handle('backend.select', (p) => ({
      backends: [...backends, cloud],
      activeBackendId: (p as { backendId: string }).backendId,
      activeModelId: 'cloud-standard',
    }));
    await app.getState().load();
    await store.getState().startLogin();

    transport.emit('account.changed', signedIn);
    await flush();

    expect(transport.callsOf('backend.select')).toEqual([{ backendId: 'cloud' }]);
    expect(app.getState().activeBackendId).toBe('cloud');
  });

  it('cancels a pending sign-in on the host when the dialog is closed', async () => {
    const { store, transport } = setup();
    await store.getState().startLogin();

    await store.getState().cancelLogin();

    expect(store.getState().dialog).toBeNull();
    expect(transport.callsOf('auth.cancelLogin')).toHaveLength(1);
  });

  it('keeps the login dialog open with the error when the sign-in cannot start', async () => {
    const { store, transport } = setup();
    transport.handle('auth.startLogin', () => {
      throw new BridgeError('network', 'offline');
    });

    await store.getState().startLogin();

    expect(store.getState().dialog).toBe('login');
    expect(store.getState().error?.code).toBe('network');
  });

  it('reports the credits added and the new balance after redeeming', async () => {
    const { store } = setup();
    store.getState().openRedeem();

    await store.getState().redeem('TEST-TEST-TEST-TEST');

    expect(store.getState().redeemOutcome).toEqual({
      kind: 'success',
      key: 'redeem.success',
      params: { added: 100, credits: 110 },
    });
  });

  it.each([
    ['NONE-NONE-NONE-NONE', 'redeem.notFound'],
    ['USED-USED-USED-USED', 'redeem.used'],
    ['GONE-GONE-GONE-GONE', 'redeem.expired'],
  ])('explains why %s could not be redeemed', async (code, key) => {
    const { store } = setup();

    await store.getState().redeem(code);

    expect(store.getState().redeemOutcome).toEqual({ kind: 'error', key });
  });

  it('opens the redeem dialog when a generation fails for lack of credits', () => {
    const { single, store } = setup();

    single.setState({ status: { kind: 'error', key: 'status.saved', error: { code: 'insufficient_credits', message: 'x' } } });

    expect(store.getState().dialog).toBe('redeem');
    expect(store.getState().redeemReason).toBe('redeem.insufficient');
  });

  it('ignores other failures', () => {
    const { single, store } = setup();

    single.setState({ status: { kind: 'error', key: 'status.saved', error: { code: 'network', message: 'x' } } });

    expect(store.getState().dialog).toBeNull();
  });

  it('pages through the credit history', async () => {
    const { store, transport } = setup();

    await store.getState().openLedger();
    expect(store.getState().ledger.map((i) => i.id)).toEqual(['l1']);
    expect(store.getState().ledgerCursor).toBe('c1');

    await store.getState().loadMoreLedger();
    expect(store.getState().ledger.map((i) => i.id)).toEqual(['l1', 'l2']);
    expect(store.getState().ledgerCursor).toBeNull();
    expect(transport.callsOf('account.ledger')[1]).toEqual({ cursor: 'c1' });
  });

  it('signs out through the host and closes the menu', async () => {
    const { store, transport } = setup();
    store.getState().toggleMenu(true);

    await store.getState().signOut();
    await flush();

    expect(store.getState().menuOpen).toBe(false);
    expect(transport.callsOf('auth.logout')).toHaveLength(1);
  });
});
