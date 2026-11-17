import type { NodeInfo, RpcParams, SessionInfo } from '../bridge/types';
import type { FakeTransport } from './fakeTransport';
import { asset } from './fixtures';

/** In-memory stand-in for the host's session.* methods, announcing changes like the host does. */
export class FakeSession {
  session: SessionInfo = { projectId: 'p1', nodes: [] };
  private counter = 0;

  constructor(private readonly transport: FakeTransport) {
    transport
      .handle('session.get', () => this.session)
      .handle('session.addNode', (p) => this.add(p as RpcParams<'session.addNode'>))
      .handle('session.setCurrent', (p) => {
        const id = (p as { id: string | null }).id;
        this.change({ ...this.session, currentNodeId: id ?? undefined });
        return null;
      })
      .handle('session.updateNode', (p) => {
        const { id, ...patch } = p as { id: string; label?: string; starred?: boolean };
        const node = { ...this.node(id), ...patch };
        this.change({ ...this.session, nodes: this.session.nodes.map((n) => (n.id === id ? node : n)) });
        return node;
      })
      .handle('session.deleteNode', (p) => {
        const { id, withDescendants } = p as { id: string; withDescendants: boolean };
        const target = this.node(id);
        const removed = new Set([id]);
        if (withDescendants) {
          for (let grew = true; grew; ) {
            grew = false;
            for (const n of this.session.nodes) {
              if (n.parentId && removed.has(n.parentId) && !removed.has(n.id)) {
                removed.add(n.id);
                grew = true;
              }
            }
          }
        }
        const nodes = this.session.nodes
          .filter((n) => !removed.has(n.id))
          .map((n) => (n.parentId === id ? { ...n, parentId: target.parentId } : n));
        const current = this.session.currentNodeId && removed.has(this.session.currentNodeId) ? target.parentId : this.session.currentNodeId;
        this.change({ ...this.session, nodes, currentNodeId: current });
        return null;
      })
      .handle('session.clear', () => {
        this.change({ ...this.session, nodes: [], currentNodeId: undefined });
        return null;
      });
  }

  /** Adds a node directly, as a finished job would on the host. */
  add(params: RpcParams<'session.addNode'>): NodeInfo {
    const node: NodeInfo = {
      id: `n${++this.counter}`,
      parentId: params.parentId,
      kind: params.kind,
      asset: asset(params.assetId),
      createdAt: new Date(2026, 0, 1, 0, 0, this.counter).toISOString(),
      label: params.label,
      starred: false,
      recipe: params.recipe,
      camera: params.camera,
    };
    this.change({ ...this.session, nodes: [...this.session.nodes, node] });
    return node;
  }

  node(id: string): NodeInfo {
    const node = this.session.nodes.find((n) => n.id === id);
    if (!node) {
      throw new Error(`unknown node ${id}`);
    }
    return node;
  }

  private change(session: SessionInfo): void {
    this.session = session;
    this.transport.emit('session.changed', session);
  }
}
