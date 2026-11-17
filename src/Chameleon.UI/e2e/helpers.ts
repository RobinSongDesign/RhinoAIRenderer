import { expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

/** Folder the development host's fake save dialogs write into (see playwright.config.ts). */
export const outputDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '.out');

/** Opens the app and waits until it has loaded its state from the host. */
export async function openApp(page: Page, language: 'en' | 'zh' = 'en'): Promise<void> {
  await page.goto('./');
  await expect(page.getByTestId('app-shell')).toBeVisible();
  const select = page.getByTestId('language-select');
  if ((await select.inputValue()) !== language) {
    await select.selectOption(language);
  }
  await expect(select).toHaveValue(language);
}

/** Calls a bridge method directly, for setting up or inspecting host state in tests. */
export async function rpc<T = unknown>(page: Page, method: string, params?: unknown): Promise<T> {
  return page.evaluate(
    async ({ method, params }) => {
      const response = await fetch(new URL('rpc', window.location.href).href, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ id: 'e2e', method, params }),
      });
      const body = await response.json();
      if (body.error) {
        throw new Error(`${method} failed: ${body.error.code}`);
      }
      return body.result;
    },
    { method, params },
  ) as Promise<T>;
}

/** Empties the output folder. */
export function clearOutput(): void {
  fs.rmSync(outputDir, { recursive: true, force: true });
  fs.mkdirSync(outputDir, { recursive: true });
}

/** Lists files in the output folder. */
export function outputFiles(): string[] {
  return fs.existsSync(outputDir) ? fs.readdirSync(outputDir).sort() : [];
}

/** The image the canvas shows (the selected history node). */
export function canvasView(page: Page) {
  return page.getByTestId('canvas-view');
}

/** The selected node in the history timeline. */
export function currentNode(page: Page) {
  return page.locator('[data-testid="history-node"][data-current="true"]');
}

/** Waits until a capture is on the canvas. */
export async function expectCaptured(page: Page): Promise<void> {
  await expect(currentNode(page)).toHaveAttribute('data-kind', 'capture');
  await expect(canvasView(page)).not.toHaveAttribute('data-asset-id', '');
}

/** Waits until a generated image is selected (generation selects its result). */
export async function expectGenerated(page: Page): Promise<void> {
  await expect(currentNode(page)).toHaveAttribute('data-kind', 'generation');
}

/** Empties the history of the host's current project; the host is shared by all tests. */
export async function resetHistory(page: Page): Promise<void> {
  await rpc(page, 'session.clear');
  await expect(page.getByTestId('history-empty')).toBeVisible();
}
