import { describe, expect, it } from 'vitest';
import type { NodeInfo } from '../../bridge/types';
import { asset } from '../../test/fixtures';
import { depths, layoutTree, visibleRange } from './treeLayout';

function node(id: string, parentId?: string): NodeInfo {
  return { id, parentId, kind: parentId ? 'generation' : 'capture', asset: asset(id), createdAt: '', starred: false };
}

const cellOf = (layout: ReturnType<typeof layoutTree>, id: string) => layout.cells.find((c) => c.node.id === id)!;

describe('layoutTree', () => {
  // a ─ b ─ c
  //   │   └ d
  //   └ e
  // f
  const nodes = [node('a'), node('b', 'a'), node('c', 'b'), node('d', 'b'), node('e', 'a'), node('f')];

  it('puts children one column right, first child on the parent row and branches below', () => {
    const layout = layoutTree(nodes);
    const at = (id: string) => [cellOf(layout, id).column, cellOf(layout, id).row];

    expect(at('a')).toEqual([0, 0]);
    expect(at('b')).toEqual([1, 0]);
    expect(at('c')).toEqual([2, 0]);
    expect(at('d')).toEqual([2, 1]);
    expect(at('e')).toEqual([1, 2]);
    expect(at('f')).toEqual([0, 3]);
    expect(layout.columns).toBe(3);
    expect(layout.rows).toBe(4);
  });

  it('never puts two nodes in the same cell', () => {
    const many = [node('r')];
    for (let i = 0; i < 30; i++) {
      many.push(node(`x${i}`, i % 3 === 0 ? 'r' : `x${i - 1}`));
    }
    const cells = layoutTree(many).cells.map((c) => `${c.column}:${c.row}`);

    expect(new Set(cells).size).toBe(cells.length);
  });

  it('shows nodes with an unknown parent as roots', () => {
    expect(cellOf(layoutTree([node('x', 'gone')]), 'x').column).toBe(0);
  });

  it('handles very deep chains', () => {
    const chain = [node('n0')];
    for (let i = 1; i < 5000; i++) {
      chain.push(node(`n${i}`, `n${i - 1}`));
    }
    expect(layoutTree(chain).columns).toBe(5000);
  });

  it('computes depths for indentation', () => {
    const d = depths(nodes);
    expect([d.get('a'), d.get('c'), d.get('e'), d.get('f')]).toEqual([0, 2, 1, 0]);
  });
});

describe('visibleRange', () => {
  it('covers the visible rows plus overscan, within bounds', () => {
    expect(visibleRange(0, 300, 60, 100, 2)).toEqual({ start: 0, end: 7 });
    expect(visibleRange(600, 300, 60, 100, 2)).toEqual({ start: 8, end: 17 });
    expect(visibleRange(5940, 300, 60, 100, 2)).toEqual({ start: 97, end: 100 });
    expect(visibleRange(0, 300, 60, 0)).toEqual({ start: 0, end: 0 });
  });
});
