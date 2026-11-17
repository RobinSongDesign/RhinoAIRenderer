import { expect, test, type Page } from '@playwright/test';
import { canvasView, currentNode, expectCaptured, expectGenerated, openApp, resetHistory, rpc } from './helpers';

const nodes = (page: Page) => page.getByTestId('history-node');
const nodeById = (page: Page, id: string) => page.locator(`[data-testid="history-node"][data-node-id="${id}"]`);

async function prepare(page: Page): Promise<void> {
  await openApp(page);
  await resetHistory(page);
  await page.getByTestId('backend-select').selectOption('fake');
}

async function generate(page: Page, prompt: string): Promise<string> {
  const before = await currentNode(page).getAttribute('data-node-id');
  await page.getByTestId('prompt-input').fill(prompt);
  await page.getByTestId('generate-button').click();
  await expectGenerated(page);
  await expect(currentNode(page)).not.toHaveAttribute('data-node-id', before ?? '');
  await expect(page.getByTestId('generate-button')).toBeEnabled();
  return (await currentNode(page).getAttribute('data-node-id')) ?? '';
}

interface HostNode {
  id: string;
  parentId?: string;
  kind: string;
}

async function hostNodes(page: Page): Promise<HostNode[]> {
  return (await rpc<{ nodes: HostNode[] }>(page, 'session.get')).nodes;
}

test('keeps every version, branches from any node, deletes and reruns', async ({ page }) => {
  await prepare(page);
  await expect(page.getByTestId('history-empty')).toBeVisible();

  // Capture, then generate twice: a chain of three nodes.
  await page.getByTestId('capture-button').click();
  await expectCaptured(page);
  const first = await generate(page, 'dusk');
  const second = await generate(page, 'night');
  await expect(nodes(page)).toHaveCount(3);
  let tree = await hostNodes(page);
  const capture = tree.find((n) => n.kind === 'capture')!;
  expect(tree.find((n) => n.id === first)!.parentId).toBe(capture.id);
  expect(tree.find((n) => n.id === second)!.parentId).toBe(first);

  // The canvas compares the selection with its parent.
  await expect(page.getByTestId('canvas-compare-slider')).toBeEnabled();

  // Select the first result and generate again: a branch.
  await nodeById(page, first).locator('img').click();
  await expect(currentNode(page)).toHaveAttribute('data-node-id', first);
  await expect(page.getByTestId('recipe-card')).toContainText('dusk');
  const branch = await generate(page, 'snow');
  tree = await hostNodes(page);
  expect(tree.filter((n) => n.parentId === first).map((n) => n.id).sort()).toEqual([second, branch].sort());

  // The tree view shows the branch one row below.
  await page.getByTestId('history-view-tree').click();
  await expect(page.getByTestId('history-tree-node')).toHaveCount(4);
  const rowOf = async (id: string) =>
    Number(await page.locator(`[data-testid="history-tree-node"][data-node-id="${id}"]`).getAttribute('data-row'));
  expect(await rowOf(branch)).not.toBe(await rowOf(second));
  await page.getByTestId('history-view-timeline').click();

  // Delete the first result only: its children move up to the capture.
  await nodeById(page, first).hover();
  await nodeById(page, first).getByTestId('history-delete').click();
  await expect(page.getByTestId('history-delete-dialog')).toContainText('2 later version(s)');
  await page.getByTestId('history-delete-only').click();
  await expect(nodes(page)).toHaveCount(3);
  tree = await hostNodes(page);
  expect(tree.filter((n) => n.parentId === capture.id).map((n) => n.id).sort()).toEqual([second, branch].sort());

  // Run the branch's recipe again: the result is its sibling.
  await nodeById(page, branch).hover();
  await nodeById(page, branch).getByTestId('history-rerun').click();
  await expect(nodes(page)).toHaveCount(4);
  await expectGenerated(page);
  const rerun = (await currentNode(page).getAttribute('data-node-id')) ?? '';
  tree = await hostNodes(page);
  expect(tree.find((n) => n.id === rerun)!.parentId).toBe(capture.id);
});

test('stars and renames nodes, and reuses a recipe', async ({ page }) => {
  await prepare(page);
  await page.getByTestId('capture-button').click();
  await expectCaptured(page);
  const result = await generate(page, 'golden hour');
  const row = nodeById(page, result);

  await row.getByTestId('history-star').click();
  await expect(row.getByTestId('history-node-name')).toContainText('★');
  await row.getByTestId('history-rename').click();
  await row.getByTestId('history-rename-input').fill('Hero shot');
  await row.getByTestId('history-rename-input').press('Enter');
  await expect(row.getByTestId('history-node-name')).toHaveText('★ Hero shot');

  await page.getByTestId('prompt-input').fill('something else');
  await page.getByTestId('recipe-apply').click();
  await expect(page.getByTestId('prompt-input')).toHaveValue('golden hour');
});

test('imports images as new roots', async ({ page }) => {
  await prepare(page);

  await page.getByTestId('history-empty').getByRole('button', { name: 'Import images' }).click();

  await expect(currentNode(page)).toHaveAttribute('data-kind', 'import');
  await expect(canvasView(page)).not.toHaveAttribute('data-asset-id', '');
});
