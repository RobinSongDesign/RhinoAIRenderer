import { useEffect, useMemo, useRef, useState } from 'react';
import type { NodeInfo } from '../../bridge/types';
import { Button } from '../../components/Button';
import { Dialog } from '../../components/Dialog';
import type { MessageKey } from '../../i18n';
import {
  useAppStore,
  useBridge,
  useServices,
  useSessionStore,
  useSingleStore,
  useT,
} from '../../state/services';
import styles from './history.module.css';
import { depths, layoutTree, visibleRange } from './treeLayout';

const ROW_HEIGHT = 58;
// Selectors must return stable values: a fresh [] on every read would re-render forever.
const NO_NODES: NodeInfo[] = [];
const CELL = { width: 84, height: 62 };
const MAX_INDENT_LEVELS = 6;

function nodeName(node: NodeInfo, t: (key: MessageKey) => string): string {
  return node.label || t(`history.kind.${node.kind}` as MessageKey);
}

/** Row of the timeline: thumbnail, name, time and (on hover or when selected) actions. */
function NodeRow({
  node,
  depth,
  top,
  current,
  onDelete,
}: {
  node: NodeInfo;
  depth: number;
  top: number;
  current: boolean;
  onDelete(node: NodeInfo): void;
}) {
  const t = useT();
  const bridge = useBridge();
  const { session, single } = useServices();
  const language = useAppStore((s) => s.language);
  const busy = useSingleStore((s) => s.activeJobId !== null);
  const [renaming, setRenaming] = useState(false);
  const time = new Date(node.createdAt).toLocaleTimeString(language === 'zh' ? 'zh-CN' : 'en-US', {
    hour: '2-digit',
    minute: '2-digit',
  });

  const finishRename = (value: string) => {
    setRenaming(false);
    if (value !== (node.label ?? '')) {
      void session.getState().update(node.id, { label: value });
    }
  };

  return (
    <div
      className={`${styles.row} ${current ? styles.current : ''}`}
      style={{ top, height: ROW_HEIGHT, paddingLeft: 6 + Math.min(depth, MAX_INDENT_LEVELS) * 10 }}
      data-testid="history-node"
      data-node-id={node.id}
      data-kind={node.kind}
      data-current={current}
      onClick={() => void session.getState().select(node.id)}
    >
      <img
        className={styles.thumb}
        src={bridge.assetUrl(node.asset.assetId)}
        alt=""
        loading="lazy"
        draggable={false}
      />
      <div className={styles.info}>
        {renaming ? (
          <input
            className={styles.rename}
            data-testid="history-rename-input"
            defaultValue={node.label ?? ''}
            autoFocus
            onClick={(e) => e.stopPropagation()}
            onBlur={(e) => finishRename(e.currentTarget.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                finishRename(e.currentTarget.value);
              } else if (e.key === 'Escape') {
                setRenaming(false);
              }
            }}
          />
        ) : (
          <span className={styles.name} data-testid="history-node-name">
            {node.starred && <span className={styles.star}>★ </span>}
            {nodeName(node, t)}
          </span>
        )}
        <span className={styles.meta}>
          {time} · {node.asset.width}×{node.asset.height}
        </span>
      </div>
      <div className={styles.actions}>
        <button
          type="button"
          className={styles.icon}
          title={t(node.starred ? 'history.unstar' : 'history.star')}
          data-testid="history-star"
          onClick={(e) => {
            e.stopPropagation();
            void session.getState().update(node.id, { starred: !node.starred });
          }}
        >
          {node.starred ? '★' : '☆'}
        </button>
        <button
          type="button"
          className={styles.icon}
          title={t('history.rename')}
          data-testid="history-rename"
          onClick={(e) => {
            e.stopPropagation();
            setRenaming(true);
          }}
        >
          ✎
        </button>
        {node.recipe && node.parentId && (
          <button
            type="button"
            className={styles.icon}
            title={t('history.rerun')}
            data-testid="history-rerun"
            disabled={busy}
            onClick={(e) => {
              e.stopPropagation();
              void single.getState().rerun(node.id);
            }}
          >
            ↻
          </button>
        )}
        <button
          type="button"
          className={styles.icon}
          title={t('history.save')}
          data-testid="history-save"
          onClick={(e) => {
            e.stopPropagation();
            void single.getState().save('png', node.id);
          }}
        >
          ⤓
        </button>
        <button
          type="button"
          className={styles.icon}
          title={t('history.delete')}
          data-testid="history-delete"
          onClick={(e) => {
            e.stopPropagation();
            onDelete(node);
          }}
        >
          🗑
        </button>
      </div>
    </div>
  );
}

/** Newest first, indented by depth; only the rows in view are rendered. */
function Timeline({
  nodes,
  currentId,
  onDelete,
}: {
  nodes: NodeInfo[];
  currentId?: string;
  onDelete(node: NodeInfo): void;
}) {
  const scroller = useRef<HTMLDivElement>(null);
  const [scroll, setScroll] = useState({ top: 0, height: 600 });
  const ordered = useMemo(() => [...nodes].reverse(), [nodes]);
  const depthOf = useMemo(() => depths(nodes), [nodes]);
  const { start, end } = visibleRange(scroll.top, scroll.height, ROW_HEIGHT, ordered.length);

  useEffect(() => {
    const element = scroller.current;
    if (!element) {
      return undefined;
    }
    const observer = new ResizeObserver(() => setScroll((s) => ({ ...s, height: element.clientHeight })));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  return (
    <div
      ref={scroller}
      className={styles.scroll}
      data-testid="history-timeline"
      onScroll={(e) => setScroll({ top: e.currentTarget.scrollTop, height: e.currentTarget.clientHeight })}
    >
      <div style={{ height: ordered.length * ROW_HEIGHT }}>
        {ordered.slice(start, end).map((node, i) => (
          <NodeRow
            key={node.id}
            node={node}
            depth={depthOf.get(node.id) ?? 0}
            top={(start + i) * ROW_HEIGHT}
            current={node.id === currentId}
            onDelete={onDelete}
          />
        ))}
      </div>
    </div>
  );
}

/** Left-to-right node graph for seeing branches; the container scrolls to pan. */
function TreeView({ nodes, currentId }: { nodes: NodeInfo[]; currentId?: string }) {
  const t = useT();
  const bridge = useBridge();
  const { session } = useServices();
  const layout = useMemo(() => layoutTree(nodes), [nodes]);
  const position = useMemo(() => new Map(layout.cells.map((c) => [c.node.id, c])), [layout]);
  const x = (column: number) => 8 + column * CELL.width;
  const y = (row: number) => 8 + row * CELL.height;

  return (
    <div className={styles.scroll} data-testid="history-tree">
      <div className={styles.tree} style={{ width: x(layout.columns) + 8, height: y(layout.rows) + 8 }}>
        <svg className={styles.treeEdges} width={x(layout.columns) + 8} height={y(layout.rows) + 8}>
          {layout.cells.map(({ node, column, row }) => {
            const parent = node.parentId ? position.get(node.parentId) : undefined;
            if (!parent) {
              return null;
            }
            const x1 = x(parent.column) + 68;
            const y1 = y(parent.row) + 26;
            const x2 = x(column);
            const y2 = y(row) + 26;
            const mid = (x1 + x2) / 2;
            return <path key={node.id} d={`M${x1},${y1} H${mid} V${y2} H${x2}`} />;
          })}
        </svg>
        {layout.cells.map(({ node, column, row }) => (
          <button
            key={node.id}
            type="button"
            className={`${styles.treeNode} ${node.id === currentId ? styles.current : ''}`}
            style={{ left: x(column), top: y(row) }}
            title={nodeName(node, t)}
            data-testid="history-tree-node"
            data-node-id={node.id}
            data-column={column}
            data-row={row}
            onClick={() => void session.getState().select(node.id)}
          >
            <img
              src={bridge.assetUrl(node.asset.assetId)}
              alt={nodeName(node, t)}
              loading="lazy"
              draggable={false}
            />
            {node.starred && <span className={`${styles.treeStar} ${styles.star}`}>★</span>}
          </button>
        ))}
      </div>
    </div>
  );
}

/** Asks whether later versions go too, when there are any. */
function DeleteDialog({ node, count, onDone }: { node: NodeInfo; count: number; onDone(): void }) {
  const t = useT();
  const { session } = useServices();
  const remove = (withDescendants: boolean) => {
    void session.getState().remove(node.id, withDescendants);
    onDone();
  };
  return (
    <Dialog
      title={t('history.deleteTitle')}
      testId="history-delete-dialog"
      onClose={onDone}
      footer={
        <>
          <Button onClick={onDone}>{t('action.cancel')}</Button>
          <Button data-testid="history-delete-only" onClick={() => remove(false)}>
            {t('history.deleteOnly')}
          </Button>
          <Button variant="danger" data-testid="history-delete-all" onClick={() => remove(true)}>
            {t('history.deleteAll')}
          </Button>
        </>
      }
    >
      {t('history.deleteQuestion', { count })}
    </Dialog>
  );
}

function ClearDialog({ onDone }: { onDone(): void }) {
  const t = useT();
  const { session } = useServices();
  return (
    <Dialog
      title={t('history.clear')}
      testId="history-clear-dialog"
      onClose={onDone}
      footer={
        <>
          <Button onClick={onDone}>{t('action.cancel')}</Button>
          <Button
            variant="danger"
            data-testid="history-clear-confirm"
            onClick={() => {
              void session.getState().clear();
              onDone();
            }}
          >
            {t('history.clear')}
          </Button>
        </>
      }
    >
      {t('history.clearConfirm')}
    </Dialog>
  );
}

/** Counts every node below `id`. */
function countDescendants(nodes: NodeInfo[], id: string): number {
  let count = 0;
  const pending = [id];
  while (pending.length > 0) {
    const parent = pending.pop();
    for (const n of nodes) {
      if (n.parentId === parent) {
        count++;
        pending.push(n.id);
      }
    }
  }
  return count;
}

/** The current project's history: timeline or tree, node actions, import and clear. */
export function HistoryPanel() {
  const t = useT();
  const { session, single } = useServices();
  const nodes = useSessionStore((s) => s.session?.nodes ?? NO_NODES);
  const currentId = useSessionStore((s) => s.session?.currentNodeId);
  const view = useSessionStore((s) => s.view);
  const capturing = useSingleStore((s) => s.capturing);
  const importing = useSingleStore((s) => s.importing);
  const [deleting, setDeleting] = useState<{ node: NodeInfo; count: number } | null>(null);
  const [clearing, setClearing] = useState(false);

  const askDelete = (node: NodeInfo) => {
    const count = countDescendants(nodes, node.id);
    if (count === 0) {
      void session.getState().remove(node.id, false);
    } else {
      setDeleting({ node, count });
    }
  };

  return (
    <div className={styles.panel} data-testid="history-panel">
      <div className={styles.header}>
        <span className={styles.title}>{t('history.title')}</span>
        <span className={styles.group} role="group">
          {(['timeline', 'tree'] as const).map((value) => (
            <Button
              key={value}
              size="small"
              variant={view === value ? 'primary' : undefined}
              aria-pressed={view === value}
              data-testid={`history-view-${value}`}
              onClick={() => session.getState().setView(value)}
            >
              {t(`history.${value}`)}
            </Button>
          ))}
        </span>
        <Button
          size="small"
          title={t('history.import')}
          aria-label={t('history.import')}
          data-testid="history-import"
          busy={importing}
          onClick={() => void single.getState().importImages()}
        >
          ⊕
        </Button>
        <Button
          size="small"
          title={t('history.clear')}
          aria-label={t('history.clear')}
          data-testid="history-clear"
          disabled={nodes.length === 0}
          onClick={() => setClearing(true)}
        >
          ⌫
        </Button>
      </div>
      {nodes.length === 0 ? (
        <div className={styles.empty} data-testid="history-empty">
          <span>{t('history.empty')}</span>
          <Button variant="primary" busy={capturing} onClick={() => void single.getState().capture()}>
            {t('action.captureActiveView')}
          </Button>
          <Button busy={importing} onClick={() => void single.getState().importImages()}>
            {t('history.import')}
          </Button>
        </div>
      ) : view === 'timeline' ? (
        <Timeline nodes={nodes} currentId={currentId} onDelete={askDelete} />
      ) : (
        <TreeView nodes={nodes} currentId={currentId} />
      )}
      {deleting && (
        <DeleteDialog node={deleting.node} count={deleting.count} onDone={() => setDeleting(null)} />
      )}
      {clearing && <ClearDialog onDone={() => setClearing(false)} />}
    </div>
  );
}
