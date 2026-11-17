import { expect, test, type Page } from '@playwright/test';
import { clearOutput, openApp, outputFiles } from './helpers';

const views = ['Exterior', 'Interior', 'Aerial'];

async function prepare(page: Page): Promise<void> {
  await openApp(page);
  await page.getByTestId('backend-select').selectOption('fake');
  await page.getByTestId('mode-batch').click();
  await page.getByTestId('prompt-input').fill('consistent render');
  await page.getByTestId('load-views-button').click();
  await expect(page.getByTestId('batch-item')).toHaveCount(3);
  await expect(page.getByTestId('status-text')).toHaveText('Loaded and captured 3/3 views');
}

function row(page: Page, view: string) {
  return page.locator(`[data-testid="batch-item"][data-view="${view}"]`);
}

test('loads and captures every named view', async ({ page }) => {
  await prepare(page);

  for (const view of views) {
    await expect(row(page, view).getByTestId('batch-item-source').locator('img')).toBeVisible();
    await expect(row(page, view).getByTestId('batch-item-status')).toHaveText('Ready');
  }
});

test('renders all views and reports progress', async ({ page }) => {
  await prepare(page);

  await page.getByTestId('start-batch-button').click();
  await expect(page.getByTestId('cancel-batch-button')).toBeVisible();

  for (const view of views) {
    await expect(row(page, view).getByTestId('batch-item-status')).toHaveText('Done ✓');
    await expect(row(page, view).getByTestId('batch-item-result').locator('img')).toBeVisible();
  }
  await expect(page.getByTestId('status-text')).toHaveText('All done! 3 succeeded, 0 failed');
  await expect(page.getByTestId('status-bar')).toContainText('3 / 3 done');
  await expect(page.getByTestId('start-batch-button')).toBeVisible();
});

test('cancelling stops the running view and everything after it', async ({ page }) => {
  await prepare(page);
  await row(page, 'Exterior').getByTestId('batch-item-addon').fill('#slow');

  await page.getByTestId('start-batch-button').click();
  await expect(row(page, 'Exterior').getByTestId('batch-item-status')).toHaveText('Generating…');
  await expect(row(page, 'Aerial').getByTestId('batch-item-status')).toHaveText('Queued…');
  await page.getByTestId('cancel-batch-button').click();

  for (const view of views) {
    await expect(row(page, view).getByTestId('batch-item-status')).toHaveText('Cancelled');
  }
  await expect(page.getByTestId('status-text')).toHaveText('Cancelled — 0/3 completed');
});

test('a failing view does not stop the others', async ({ page }) => {
  await prepare(page);
  await row(page, 'Interior').getByTestId('batch-item-addon').fill('#fail:provider_error');

  await page.getByTestId('start-batch-button').click();

  await expect(row(page, 'Exterior').getByTestId('batch-item-status')).toHaveText('Done ✓');
  await expect(row(page, 'Interior').getByTestId('batch-item-status')).toHaveText('Failed ✗');
  await expect(row(page, 'Interior').getByTestId('error-notice')).toHaveAttribute(
    'data-error-code',
    'provider_error',
  );
  await expect(row(page, 'Aerial').getByTestId('batch-item-status')).toHaveText('Done ✓');
  await expect(page.getByTestId('status-text')).toHaveText('All done! 2 succeeded, 1 failed');
});

test('regenerates and recaptures a single view', async ({ page }) => {
  await prepare(page);
  await page.getByTestId('start-batch-button').click();
  await expect(page.getByTestId('status-text')).toHaveText('All done! 3 succeeded, 0 failed');
  const before = await row(page, 'Interior')
    .getByTestId('batch-item-result')
    .locator('img')
    .getAttribute('data-asset-id');

  await row(page, 'Interior').getByTestId('batch-item-regenerate').click();
  await expect(row(page, 'Interior').getByTestId('batch-item-status')).toHaveText('Done ✓');
  await expect(row(page, 'Interior').getByTestId('batch-item-result').locator('img')).not.toHaveAttribute(
    'data-asset-id',
    before ?? '',
  );

  await row(page, 'Aerial').getByTestId('batch-item-recapture').click();
  await expect(row(page, 'Aerial').getByTestId('batch-item-status')).toHaveText('Ready');
});

test('select all and deselect all control what is rendered', async ({ page }) => {
  await prepare(page);

  await page.getByTestId('deselect-all').click();
  await expect(page.getByTestId('start-batch-button')).toBeDisabled();
  await row(page, 'Aerial').getByTestId('batch-item-check').check();
  await page.getByTestId('start-batch-button').click();

  await expect(row(page, 'Aerial').getByTestId('batch-item-status')).toHaveText('Done ✓');
  await expect(row(page, 'Exterior').getByTestId('batch-item-status')).toHaveText('Ready');
  await page.getByTestId('select-all').click();
  await expect(page.getByTestId('batch-item-check').first()).toBeChecked();
});

test('saves every result into the chosen folder, named after the views', async ({ page }) => {
  clearOutput();
  await prepare(page);
  await page.getByTestId('start-batch-button').click();
  await expect(page.getByTestId('status-text')).toHaveText('All done! 3 succeeded, 0 failed');

  await page.getByTestId('save-all-button').click();

  await expect(page.getByTestId('status-text')).toContainText('Saved 3 image(s) to:');
  expect(outputFiles()).toEqual(['Aerial.jpg', 'Exterior.jpg', 'Interior.jpg']);
});

test('a dropped BMP becomes the style reference', async ({ page }) => {
  await prepare(page);

  // A 2×2 24-bit BMP: not accepted by the host, so the UI converts it to PNG first.
  await page.evaluate(() => {
    const bytes = new Uint8Array([
      0x42, 0x4d, 70, 0, 0, 0, 0, 0, 0, 0, 54, 0, 0, 0, 40, 0, 0, 0, 2, 0, 0, 0, 2, 0, 0, 0, 1, 0, 24, 0, 0,
      0, 0, 0, 16, 0, 0, 0, 0x13, 0x0b, 0, 0, 0x13, 0x0b, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255, 0, 255, 0,
      0, 0, 0, 255, 255, 255, 255, 0, 0,
    ]);
    const transfer = new DataTransfer();
    transfer.items.add(new File([bytes], 'reference.bmp', { type: 'image/bmp' }));
    const zone = document.querySelector('[data-testid="reference-drop"]');
    zone?.dispatchEvent(new DragEvent('drop', { dataTransfer: transfer, bubbles: true, cancelable: true }));
  });

  const image = page.getByTestId('reference-drop').locator('img');
  await expect(image).toBeVisible();
  await page.getByTestId('start-batch-button').click();
  await expect(page.getByTestId('status-text')).toHaveText('All done! 3 succeeded, 0 failed');
});
