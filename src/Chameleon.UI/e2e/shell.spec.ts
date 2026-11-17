import { expect, test } from '@playwright/test';
import { openApp, rpc } from './helpers';

test('loads the application shell from the host', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(String(error)));

  await openApp(page);

  await expect(page.getByTestId('mode-single')).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByTestId('status-text')).toHaveText('Ready');
  expect(errors).toEqual([]);
});

test('lists the backends offered by the host', async ({ page }) => {
  await openApp(page);

  const state = await rpc<{ backends: { id: string }[] }>(page, 'app.getState');

  expect(state.backends.map((b) => b.id)).toEqual(['direct:Gemini', 'direct:BltAI', 'cloud', 'fake']);
});

test('switches language immediately and keeps it after a reload', async ({ page }) => {
  await openApp(page, 'en');
  await expect(page.getByTestId('mode-batch')).toHaveText('Batch');

  await page.getByTestId('language-select').selectOption('zh');

  await expect(page.getByTestId('mode-batch')).toHaveText('批量');
  await expect(page.getByTestId('status-text')).toHaveText('就绪');
  await page.reload();
  await expect(page.getByTestId('mode-batch')).toHaveText('批量');

  await page.getByTestId('language-select').selectOption('en');
  await expect(page.getByTestId('mode-batch')).toHaveText('Batch');
});

test('switches between single and batch mode', async ({ page }) => {
  await openApp(page);

  await page.getByTestId('mode-batch').click();

  await expect(page.getByTestId('app-shell')).toHaveAttribute('data-mode', 'batch');
  await expect(page.getByTestId('mode-batch')).toHaveAttribute('aria-selected', 'true');
});
