import { expect, test, type Page } from '@playwright/test';
import { openApp } from './helpers';

const canvas = (page: Page) => page.getByTestId('canvas-view');
const panes = (page: Page) => page.getByTestId('canvas-view-panes');

async function zoom(page: Page): Promise<number> {
  return Number(await canvas(page).getAttribute('data-zoom'));
}

async function captureAndGenerate(page: Page): Promise<{ source: string; result: string }> {
  await openApp(page);
  await page.getByTestId('backend-select').selectOption('fake');
  await page.getByTestId('capture-button').click();
  const sourceImg = page.getByTestId('source-view').locator('img');
  await expect(sourceImg).toBeVisible();
  const source = (await sourceImg.getAttribute('data-asset-id')) ?? '';
  await page.getByTestId('prompt-input').fill('render');
  await page.getByTestId('generate-button').click();
  const resultImg = page.getByTestId('result-view').locator('img');
  await expect(resultImg).toBeVisible();
  const result = (await resultImg.getAttribute('data-asset-id')) ?? '';
  await expect(canvas(page)).toHaveAttribute('data-asset-id', result);
  await expect(canvas(page)).not.toHaveAttribute('data-zoom', '');
  return { source, result };
}

test('zooms at the pointer, fits on double-click and shows 100% with the 1 key', async ({ page }) => {
  await openApp(page);
  await page.getByTestId('capture-button').click();
  await expect(canvas(page)).not.toHaveAttribute('data-zoom', '');
  await expect(page.getByTestId('canvas-view-status')).toContainText('1280×720');
  const fitted = await zoom(page);

  const box = (await panes(page).boundingBox())!;
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.wheel(0, -300);
  await expect.poll(() => zoom(page)).toBeGreaterThan(fitted);
  await expect(page.getByTestId('canvas-view-zoom')).toHaveText(`${await zoom(page)}%`);
  // The status bar follows the pointer in image pixels.
  await page.mouse.move(box.x + box.width / 2 + 1, box.y + box.height / 2);
  await expect(page.getByTestId('canvas-view-status')).toContainText(/\d+, \d+/);

  await panes(page).dblclick();
  await expect.poll(() => zoom(page)).toBe(fitted);

  await page.keyboard.press('1');
  await expect.poll(() => zoom(page)).toBe(100);
  await page.getByTestId('canvas-fit').click();
  await expect.poll(() => zoom(page)).toBe(fitted);
});

test('compares the result with its source', async ({ page }) => {
  const { source, result } = await captureAndGenerate(page);
  await expect(page.getByTestId('canvas-compare-slider')).toBeEnabled();

  // Slider: dragging moves the divider.
  await page.getByTestId('canvas-compare-slider').click();
  await expect(canvas(page)).toHaveAttribute('data-compare', 'slider');
  const box = (await panes(page).boundingBox())!;
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width * 0.2, box.y + box.height / 2, { steps: 4 });
  await page.mouse.up();
  await expect(canvas(page)).toHaveAttribute('data-split', '20');

  // Side by side: two synchronized panes.
  await page.getByTestId('canvas-compare-sideBySide').click();
  await expect(panes(page).locator('canvas')).toHaveCount(2);

  // Holding the backslash key shows the source.
  await page.getByTestId('canvas-compare-off').click();
  await page.mouse.move(box.x + 20, box.y + 20);
  await page.keyboard.down('\\');
  await expect(canvas(page)).toHaveAttribute('data-peek', 'true');
  await page.keyboard.up('\\');
  await expect(canvas(page)).toHaveAttribute('data-peek', 'false');

  // The thumbnails choose what the canvas shows; the source has nothing to compare with.
  await page.getByTestId('source-view').locator('img').click();
  await expect(canvas(page)).toHaveAttribute('data-asset-id', source);
  await expect(page.getByTestId('canvas-compare-slider')).toBeDisabled();
  await page.getByTestId('result-view').locator('img').click();
  await expect(canvas(page)).toHaveAttribute('data-asset-id', result);
});

test('pans with the space bar held, without scrolling the page', async ({ page }) => {
  await captureAndGenerate(page);
  const box = (await panes(page).boundingBox())!;
  const center = { x: box.x + box.width / 2, y: box.y + box.height / 2 };
  // Shortcuts apply while the pointer is over the canvas.
  await page.mouse.move(center.x, center.y);
  await page.keyboard.press('1');
  await expect.poll(() => zoom(page)).toBe(100);
  const status = page.getByTestId('canvas-view-status');
  const before = await status.textContent();

  await page.keyboard.down(' ');
  await page.mouse.down();
  await page.mouse.move(center.x + 60, center.y + 40, { steps: 4 });
  await page.mouse.up();
  await page.keyboard.up(' ');
  await page.mouse.move(center.x + 60, center.y + 40);

  // The same screen point now lies over the pixel that was under the pointer before the drag.
  await expect(status).toHaveText(before ?? '');
  expect(await page.evaluate(() => window.scrollY)).toBe(0);
});
