import { createStore, type StoreApi } from 'zustand/vanilla';
import type { Bridge } from '../../bridge/Bridge';
import type { NodeInfo, RpcParams, SessionInfo } from '../../bridge/types';
import type { AppStoreState } from '../../state/appStore';

export type HistoryView = 'timeline' | 'tree';

export interface SessionStoreState {
  session: SessionInfo | null;
  view: HistoryView;

  /** Fetches the history from the host (also after a reconnect). */
  load(): Promise<void>;
  /** Selects a node; the selection shows immediately and the host confirms. */
  select(id: string | null): Promise<void>;
  /** Adds a node and selects it. */
  add(params: RpcParams<'session.addNode'>): Promise<NodeInfo>;
  update(id: string, patch: { label?: string; starred?: boolean }): Promise<void>;
  remove(id: string, withDescendants: boolean): Promise<void>;
  clear(): Promise<void>;
  setView(view: HistoryView): void;
}

/** The current project's history tree, mirrored from the host. */
export function createSessionStore(bridge: Bridge, app: StoreApi<AppStoreState>): StoreApi<SessionStoreState> {
  const store = createStore<SessionStoreState>()((set, get) => ({
    session: null,
    view: 'timeline',

    async load() {
      set({ session: await bridge.call('session.get') });
    },

    async select(id) {
      const session = get().session;
      if (session) {
        set({ session: { ...session, currentNodeId: id ?? undefined } });
      }
      await bridge.call('session.setCurrent', { id });
    },

    async add(params) {
      const node = await bridge.call('session.addNode', params);
      // The session.changed event may come before or after this answer; make sure the node is there.
      mergeNode(node);
      await get().select(node.id);
      return node;
    },

    async update(id, patch) {
      mergeNode(await bridge.call('session.updateNode', { id, ...patch }));
    },

    async remove(id, withDescendants) {
      await bridge.call('session.deleteNode', { id, withDescendants });
    },

    async clear() {
      await bridge.call('session.clear');
    },

    setView: (view) => set({ view }),
  }));

  function mergeNode(node: NodeInfo): void {
    const session = store.getState().session;
    if (!session) {
      return;
    }
    const exists = session.nodes.some((n) => n.id === node.id);
    store.setState({
      session: {
        ...session,
        nodes: exists ? session.nodes.map((n) => (n.id === node.id ? node : n)) : [...session.nodes, node],
      },
    });
  }

  bridge.on('session.changed', (session) => store.setState({ session }));
  // Events sent while the stream was down are lost: start again from a full snapshot.
  app.subscribe((state, previous) => {
    if (state.loadState === 'ready' && (previous.loadState !== 'ready' || (state.connection === 'connected' && previous.connection === 'disconnected'))) {
      void store.getState().load().catch(() => undefined);
    }
  });

  return store;
}

/** The selected node, if any. */
export function selectCurrentNode(state: SessionStoreState): NodeInfo | null {
  const id = state.session?.currentNodeId;
  return (id && state.session?.nodes.find((n) => n.id === id)) || null;
}

/** The parent of the selected node, if any. */
export function selectParentNode(state: SessionStoreState): NodeInfo | null {
  const current = selectCurrentNode(state);
  return (current?.parentId && state.session?.nodes.find((n) => n.id === current.parentId)) || null;
}

/** Finds a node by id. */
export function findNode(state: SessionStoreState, id: string | undefined | null): NodeInfo | null {
  return (id && state.session?.nodes.find((n) => n.id === id)) || null;
}
