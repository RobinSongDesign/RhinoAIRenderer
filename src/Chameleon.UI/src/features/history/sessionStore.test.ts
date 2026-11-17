import { describe, expect, it } from 'vitest';
import { Bridge } from '../../bridge/Bridge';
import { createAppStore } from '../../state/appStore';
import { FakeSession } from '../../test/fakeSession';
import { FakeTransport, flush } from '../../test/fakeTransport';
import { appState } from '../../test/fixtures';
import { createSessionStore, selectCurrentNode, selectParentNode } from './sessionStore';

async function setup() {
  const transport = new FakeTransport().handle('app.getState', () => appState());
  const host = new FakeSession(transport);
  const bridge = new Bridge(transport);
  const app = createAppStore(bridge, 'en');
  const session = createSessionStore(bridge, app);
  await app.getState().load();
  await flush();
  return { transport, host, app, session };
}

describe('sessionStore', () => {
  it('loads the history once the app state is there and follows host events', async () => {
    const { host, session } = await setup();
    expect(session.getState().session?.projectId).toBe('p1');

    host.add({ kind: 'capture', assetId: 'a' });

    expect(session.getState().session?.nodes).toHaveLength(1);
  });

  it('adds nodes and selects them', async () => {
    const { session } = await setup();

    const root = await session.getState().add({ kind: 'capture', assetId: 'a' });
    const child = await session.getState().add({ kind: 'import', assetId: 'b', parentId: root.id });

    expect(selectCurrentNode(session.getState())?.id).toBe(child.id);
    expect(selectParentNode(session.getState())?.id).toBe(root.id);
  });

  it('shows a selection before the host confirms it', async () => {
    const { host, session, transport } = await setup();
    const node = host.add({ kind: 'capture', assetId: 'a' });
    let release: () => void = () => undefined;
    transport.handle('session.setCurrent', () => new Promise((resolve) => (release = () => resolve(null))));

    const pending = session.getState().select(node.id);

    expect(session.getState().session?.currentNodeId).toBe(node.id);
    release();
    await pending;
  });

  it('updates, removes and clears through the host', async () => {
    const { session } = await setup();
    const root = await session.getState().add({ kind: 'capture', assetId: 'a' });
    const child = await session.getState().add({ kind: 'generation', assetId: 'b', parentId: root.id });

    await session.getState().update(child.id, { starred: true, label: 'Best' });
    expect(session.getState().session?.nodes[1]).toMatchObject({ starred: true, label: 'Best' });

    await session.getState().remove(root.id, false);
    expect(session.getState().session?.nodes.map((n) => [n.id, n.parentId])).toEqual([[child.id, undefined]]);

    await session.getState().clear();
    expect(session.getState().session?.nodes).toEqual([]);
  });

  it('reloads after the connection comes back', async () => {
    const { app, session, transport } = await setup();
    const loads = () => transport.callsOf('session.get').length;
    const before = loads();

    transport.setConnection('connected');
    transport.setConnection('disconnected');
    transport.setConnection('connected');
    await flush();
    await flush();

    expect(app.getState().connection).toBe('connected');
    expect(loads()).toBeGreaterThan(before);
    expect(session.getState().session).not.toBeNull();
  });
});
