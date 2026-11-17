import type { NodeInfo } from '../../bridge/types';

/** Position of a node in the tree view: `column` is its depth, `row` its line. */
export interface TreeCell {
  node: NodeInfo;
  column: number;
  row: number;
}

export interface TreeLayout {
  cells: TreeCell[];
  columns: number;
  rows: number;
}

/**
 * Lays a forest out left to right: each node one column right of its parent, the first child on
 * its parent's row and every further branch on new rows below, so branches never overlap.
 * Siblings keep the order they were added in; roots are stacked in the same way.
 */
export function layoutTree(nodes: NodeInfo[]): TreeLayout {
  const ids = new Set(nodes.map((n) => n.id));
  const children = new Map<string | undefined, NodeInfo[]>();
  for (const node of nodes) {
    // A node whose parent is unknown is shown as a root rather than lost.
    const parent = node.parentId && ids.has(node.parentId) ? node.parentId : undefined;
    const list = children.get(parent) ?? [];
    list.push(node);
    children.set(parent, list);
  }

  const cells: TreeCell[] = [];
  let nextRow = 0;
  let columns = 0;
  // Iterative depth-first walk, so very deep chains cannot overflow the stack.
  const stack: { node: NodeInfo; column: number; row: number | null }[] = [];
  const pushChildren = (parent: string | undefined, column: number, firstRow: number | null) => {
    const list = children.get(parent) ?? [];
    for (let i = list.length - 1; i >= 0; i--) {
      stack.push({ node: list[i], column, row: i === 0 ? firstRow : null });
    }
  };
  pushChildren(undefined, 0, null);
  while (stack.length > 0) {
    const item = stack.pop()!;
    const row = item.row ?? nextRow;
    nextRow = Math.max(nextRow, row + 1);
    cells.push({ node: item.node, column: item.column, row });
    columns = Math.max(columns, item.column + 1);
    pushChildren(item.node.id, item.column + 1, row);
  }
  return { cells, columns, rows: nextRow };
}

/** Depth of every node (0 for roots), for indenting the timeline. */
export function depths(nodes: NodeInfo[]): Map<string, number> {
  const byId = new Map(nodes.map((n) => [n.id, n]));
  const result = new Map<string, number>();
  const depthOf = (node: NodeInfo): number => {
    const known = result.get(node.id);
    if (known !== undefined) {
      return known;
    }
    let depth = 0;
    const seen = new Set<string>();
    for (let p = node.parentId ? byId.get(node.parentId) : undefined; p && !seen.has(p.id); p = p.parentId ? byId.get(p.parentId) : undefined) {
      seen.add(p.id);
      depth++;
    }
    result.set(node.id, depth);
    return depth;
  };
  nodes.forEach(depthOf);
  return result;
}

/** Rows to render for a scrolled list with fixed row height (plus a few above and below). */
export function visibleRange(
  scrollTop: number,
  viewportHeight: number,
  rowHeight: number,
  count: number,
  overscan = 4,
): { start: number; end: number } {
  const start = Math.max(0, Math.floor(scrollTop / rowHeight) - overscan);
  const end = Math.min(count, Math.ceil((scrollTop + viewportHeight) / rowHeight) + overscan);
  return { start, end: Math.max(start, end) };
}
