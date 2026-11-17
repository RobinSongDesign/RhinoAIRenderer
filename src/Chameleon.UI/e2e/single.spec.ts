import { expect, test, type Page } from '@playwright/test';
import { clearOutput, openApp, outputFiles } from './helpers';

async function prepare(page: Page): Promise<void> {
  await openApp(page);
  await page.getByTestId('backend-select').selectOption('fake');
  await expect(page.getByTestId('model-select')).toHaveValue('fake-fast');
}

async function captureAndGenerate(page: Page, prompt: string): Promise<void> {
  await page.getByTestId('capture-button').click();
  await expect(page.getByTestId('source-view').locator('img')).toBeVisible();
  await page.getByTestId('prompt-input').fill(prompt);
  await page.getByTestId('generate-button').click();
}

test('captures, applies a style template and generates a result', async ({ page }) => {
  await prepare(page);

  await page.getByTestId('capture-button').click();
  await expect(page.getByTestId('source-view').locator('img')).toBeVisible();
  await expect(page.getByTestId('status-text')).toHaveText('Captured: 1280×720');

  await page.getByTestId('style-select').selectOption('dusk');
  await expect(page.getByTestId('prompt-input')).toHaveValue(/Golden hour architectural photography/);

  await page.getByTestId('generate-button').click();
  await expect(page.getByTestId('cancel-button')).toBeVisible();
  await expect(page.getByTestId('result-view').locator('img')).toBeVisible();
  await expect(page.getByTestId('status-text')).toHaveText('Generated: 1280×720');
  await expect(page.getByTestId('generate-button')).toBeEnabled();
});

test('choosing the Custom template keeps the prompt', async ({ page }) => {
  await prepare(page);
  await page.getByTestId('prompt-input').fill('my own words');

  await page.getByTestId('style-select').selectOption('custom');

  await expect(page.getByTestId('prompt-input')).toHaveValue('my own words');
});

test('shows a localized error with the provider detail when generation fails', async ({ page }) => {
  await prepare(page);

  await captureAndGenerate(page, 'render #fail:rate_limited');

  const notice = page.getByTestId('error-notice');
  await expect(notice).toHaveAttribute('data-error-code', 'rate_limited');
  await expect(notice).toContainText('Too many requests');
  await notice.getByRole('button', { name: 'Details' }).click();
  await expect(notice).toContainText('fake provider message for rate_limited');
});

test('cancels a running generation', async ({ page }) => {
  await prepare(page);
  await captureAndGenerate(page, 'render #slow');
  await expect(page.getByTestId('cancel-button')).toBeVisible();

  await page.getByTestId('cancel-button').click();

  await expect(page.getByTestId('status-text')).toHaveText('Cancelled');
  await expect(page.getByTestId('generate-button')).toBeVisible();
  await expect(page.getByTestId('result-view').locator('img')).toHaveCount(0);
});

test('saves the result as PNG and as JPEG', async ({ page }) => {
  clearOutput();
  await prepare(page);
  await captureAndGenerate(page, 'render');
  await expect(page.getByTestId('result-view').locator('img')).toBeVisible();

  await page.getByTestId('save-button').click();
  await expect(page.getByTestId('status-text')).toContainText('Saved to:');
  await page.getByTestId('format-select').selectOption('jpg');
  await page.getByTestId('save-button').click();
  await expect.poll(() => outputFiles().length).toBe(2);

  // The sample result is a JPEG, so PNG needed a conversion and JPEG did not.
  const files = outputFiles();
  expect(files.some((f) => /^AIRender_\d{8}_\d{6}\.png$/.test(f))).toBe(true);
  expect(files.some((f) => /^AIRender_\d{8}_\d{6}\.jpg$/.test(f))).toBe(true);
});

test('uses the result as the new source, then clears everything', async ({ page }) => {
  await prepare(page);
  await captureAndGenerate(page, 'render');
  const result = page.getByTestId('result-view').locator('img');
  await expect(result).toBeVisible();
  const resultId = await result.getAttribute('data-asset-id');

  await page.getByTestId('use-as-source-button').click();
  await expect(page.getByTestId('source-view').locator('img')).toHaveAttribute(
    'data-asset-id',
    resultId ?? '',
  );
  await expect(result).toHaveCount(0);

  await page.getByTestId('clear-button').click();
  await expect(page.getByTestId('source-view').locator('img')).toHaveCount(0);
  await expect(page.getByTestId('prompt-input')).toHaveValue('');
  await expect(page.getByTestId('generate-button')).toBeDisabled();
});

test('opens and closes the full-screen preview', async ({ page }) => {
  await prepare(page);
  await page.getByTestId('capture-button').click();
  await expect(page.getByTestId('canvas-fullscreen')).toBeEnabled();

  await page.getByTestId('canvas-fullscreen').click();
  await expect(page.getByTestId('preview-overlay')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByTestId('preview-overlay')).toHaveCount(0);

  await page.getByTestId('canvas-fullscreen').click();
  await page.getByTestId('preview-overlay').click();
  await expect(page.getByTestId('preview-overlay')).toHaveCount(0);
});

test('a style reference can be chosen and removed in single mode', async ({ page }) => {
  await prepare(page);

  await page.getByTestId('reference-drop').click();
  await expect(page.getByTestId('reference-drop').locator('img')).toBeVisible();
  await page.getByTestId('reference-drop').getByRole('button', { name: 'Remove reference' }).click();

  await expect(page.getByTestId('reference-drop').locator('img')).toHaveCount(0);
});

test('points to the settings when the provider has no key', async ({ page }) => {
  await openApp(page);
  await page.getByTestId('backend-select').selectOption('direct:Gemini');

  await expect(page.getByTestId('backend-not-ready')).toBeVisible();
  await page.getByTestId('capture-button').click();
  await page.getByTestId('prompt-input').fill('render');
  await page.getByTestId('generate-button').click();

  await expect(page.getByTestId('error-notice')).toHaveAttribute('data-error-code', 'not_ready');
});
