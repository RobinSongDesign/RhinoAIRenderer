import { expect, test, type Page } from '@playwright/test';
import { expectGenerated, openApp } from './helpers';

/** The only key the development host's fake providers accept. */
const GOOD_KEY = 'dev-good-key';

async function openSettings(page: Page): Promise<void> {
  await page.getByTestId('open-settings').click();
  await expect(page.getByTestId('settings-dialog')).toBeVisible();
}

async function addProvider(page: Page, name: string, url: string, models: string): Promise<void> {
  await page.getByTestId('provider-add').click();
  await page.getByTestId('provider-name').fill(name);
  await page.getByTestId('provider-url').fill(url);
  await page.getByTestId('provider-models').fill(models);
  await page.getByTestId('provider-save').click();
  await expect(page.getByTestId('provider-details')).toBeVisible();
}

test('adds, edits and deletes a custom provider, falling back when it was active', async ({ page }) => {
  await openApp(page);
  await openSettings(page);

  await addProvider(page, 'My Relay', 'https://relay.example.com/', 'model-x, model-y');
  await expect(page.getByTestId('provider-detail-url')).toHaveText('https://relay.example.com');
  await expect(page.getByTestId('provider-detail-models')).toHaveText('model-x, model-y');

  await page.getByTestId('provider-edit').click();
  await page.getByTestId('provider-name').fill('Renamed Relay');
  await page.getByTestId('provider-save').click();
  await expect(page.getByTestId('provider-select').locator('option:checked')).toHaveText('Renamed Relay');

  await page.getByTestId('settings-close').click();
  await page.getByTestId('backend-select').selectOption({ label: 'Renamed Relay' });
  await expect(page.getByTestId('model-select')).toHaveValue('model-x');

  await openSettings(page);
  await expect(page.getByTestId('provider-select').locator('option:checked')).toHaveText('Renamed Relay');
  await page.getByTestId('provider-delete').click();
  await page.getByTestId('provider-delete-confirm').click();
  await page.getByTestId('settings-close').click();

  await expect(page.getByTestId('backend-select')).toHaveValue('direct:Chameleon');
  await expect(
    page.getByTestId('backend-select').locator('option', { hasText: 'Renamed Relay' }),
  ).toHaveCount(0);
});

test('validates the provider form', async ({ page }) => {
  await openApp(page);
  await openSettings(page);
  await page.getByTestId('provider-add').click();

  await page.getByTestId('provider-save').click();
  await expect(page.getByTestId('settings-message')).toHaveText('A name is required');
  await page.getByTestId('provider-name').fill('X');
  await page.getByTestId('provider-url').fill('relay.example.com');
  await page.getByTestId('provider-save').click();
  await expect(page.getByTestId('settings-message')).toHaveText(
    'The API URL must start with http:// or https://',
  );
});

test('choosing the OpenAI format fills defaults and locks bearer authentication', async ({ page }) => {
  await openApp(page);
  await openSettings(page);
  await page.getByTestId('provider-add').click();

  await page.getByTestId('provider-format').selectOption('openai');

  await expect(page.getByTestId('provider-url')).toHaveValue('https://api.openai.com');
  await expect(page.getByTestId('provider-models')).toHaveValue('gpt-image-2');
  await expect(page.getByTestId('provider-auth')).toHaveValue('bearer');
  await expect(page.getByTestId('provider-auth')).toBeDisabled();
  await expect(page.getByTestId('provider-search')).toHaveCount(0);
});

test('tests keys: a wrong key fails with a reason, the right one passes', async ({ page }) => {
  await openApp(page);
  await openSettings(page);
  await page.getByTestId('provider-select').selectOption('Gemini');

  await page.getByTestId('key-input').fill('definitely-wrong');
  await page.getByTestId('key-test').click();
  await expect(page.getByTestId('error-notice')).toHaveAttribute('data-error-code', 'auth_failed');

  await page.getByTestId('key-input').fill(GOOD_KEY);
  await page.getByTestId('key-test').click();
  await expect(page.getByTestId('settings-message')).toHaveText('API key is valid!');
  // Testing never stores the key.
  await expect(page.getByTestId('key-clear')).toHaveCount(0);
});

test('storing a key makes the provider ready, and it can then generate', async ({ page }) => {
  await openApp(page);
  await page.getByTestId('backend-select').selectOption('direct:Chameleon');
  await expect(page.getByTestId('backend-not-ready')).toBeVisible();

  await page.getByTestId('backend-not-ready').getByRole('button').click();
  await expect(page.getByTestId('provider-details')).toHaveAttribute('data-provider', 'Chameleon');
  await page.getByTestId('key-input').fill(GOOD_KEY);
  await page.getByTestId('key-save').click();
  await expect(page.getByTestId('settings-message')).toHaveText('API key saved');
  await expect(page.getByTestId('key-input')).toHaveValue('');
  await expect(page.getByTestId('key-input')).toHaveAttribute('placeholder', 'Saved — type to replace');
  await page.getByTestId('settings-close').click();

  await expect(page.getByTestId('backend-not-ready')).toHaveCount(0);
  await page.getByTestId('capture-button').click();
  await page.getByTestId('prompt-input').fill('render through the provider');
  await page.getByTestId('generate-button').click();
  await expectGenerated(page);

  await openSettings(page);
  await page.getByTestId('provider-select').selectOption('Chameleon');
  await page.getByTestId('key-clear').click();
  await expect(page.getByTestId('settings-message')).toHaveText('API key cleared');
});

test('a stored key never reaches the page', async ({ page }) => {
  const responses: string[] = [];
  page.on('response', async (response) => {
    if (response.url().endsWith('/rpc')) {
      responses.push(await response.text());
    }
  });
  await openApp(page);
  await openSettings(page);
  await page.getByTestId('provider-select').selectOption('Gemini');
  await page.getByTestId('key-input').fill(GOOD_KEY);
  await page.getByTestId('key-save').click();
  await expect(page.getByTestId('key-clear')).toBeVisible();

  await page.getByTestId('settings-close').click();
  await page.reload();
  await openSettings(page);
  await page.getByTestId('provider-select').selectOption('Gemini');
  await page.getByTestId('key-test').click();
  await expect(page.getByTestId('settings-message')).toHaveText('API key is valid!');

  expect(await page.content()).not.toContain(GOOD_KEY);
  // Only the request that set the key carried it; no answer ever contains it.
  expect(responses.join('\n')).not.toContain(GOOD_KEY);
  await page.getByTestId('key-clear').click();
});

test('changes the language for the whole interface from settings', async ({ page }) => {
  await openApp(page, 'en');
  await openSettings(page);

  await page.getByTestId('settings-dialog').getByTestId('language-select').selectOption('zh');

  await expect(page.getByTestId('settings-dialog')).toContainText('服务商');
  await expect(page.getByTestId('mode-single')).toHaveText('单图');
  await page.getByTestId('settings-dialog').getByTestId('language-select').selectOption('en');
  await expect(page.getByTestId('mode-single')).toHaveText('Single');
});

test('edits and restores the system prompt, and keeps the batch mode', async ({ page }) => {
  await openApp(page);
  await openSettings(page);
  const editor = page.getByTestId('system-prompt');
  const original = await editor.inputValue();
  expect(original).toContain('This is a render image.');

  await editor.fill('Keep every edge straight.');
  await page.getByTestId('system-prompt-save').click();
  await page.getByTestId('batch-mode').selectOption('anchorParallel');
  await page.reload();
  await openSettings(page);

  await expect(page.getByTestId('system-prompt')).toHaveValue('Keep every edge straight.');
  await expect(page.getByTestId('batch-mode')).toHaveValue('anchorParallel');
  await page.getByTestId('system-prompt-reset').click();
  await expect(page.getByTestId('system-prompt')).toHaveValue(original);
  await page.getByTestId('batch-mode').selectOption('serial');
});

test('shows version information and closes with Escape', async ({ page }) => {
  await openApp(page);
  await openSettings(page);

  await expect(page.getByTestId('about-version')).toHaveText('dev');
  await page.keyboard.press('Escape');
  await expect(page.getByTestId('settings-dialog')).toHaveCount(0);
});

test('a relay provider shows its balance and points to its top-up page when it runs out', async ({ page }) => {
  await openApp(page);
  await openSettings(page);
  await page.getByTestId('provider-add').click();
  await page.getByTestId('provider-name').fill('Balance Relay');
  await page.getByTestId('provider-url').fill('https://relay.example.com');
  await page.getByTestId('provider-models').fill('gemini-3.1-flash-image-preview');
  await page.getByTestId('provider-balance').check();
  await page.getByTestId('provider-topup').fill('http://relay.example.com/topup');
  await page.getByTestId('provider-save').click();
  await expect(page.getByTestId('settings-message')).toContainText('https://');
  await page.getByTestId('provider-topup').fill('https://relay.example.com/topup');
  await page.getByTestId('provider-save').click();
  await expect(page.getByTestId('provider-details')).toBeVisible();
  await page.getByTestId('key-input').fill(GOOD_KEY);
  await page.getByTestId('key-save').click();
  await expect(page.getByTestId('settings-message')).toHaveText('API key saved');
  await page.getByTestId('settings-close').click();

  await page.getByTestId('backend-select').selectOption({ label: 'Balance Relay' });
  const balance = page.getByTestId('balance-text');
  await expect(balance).toHaveText(/^Balance: \$\d+\.\d{2}$/);
  const before = await balance.textContent();
  await expect(page.getByTestId('balance-topup')).toBeVisible();

  // A generation costs something: the balance refreshes by itself.
  await page.getByTestId('capture-button').click();
  await page.getByTestId('prompt-input').fill('render through the relay');
  await page.getByTestId('generate-button').click();
  await expectGenerated(page);
  await expect(balance).not.toHaveText(before ?? '');

  // Out of balance: a clear message, and no account-mode redeem dialog.
  await page.getByTestId('prompt-input').fill('render #quota');
  await page.getByTestId('generate-button').click();
  await expect(page.getByTestId('error-notice')).toHaveAttribute('data-error-code', 'insufficient_credits');
  await expect(page.getByTestId('redeem-dialog')).toHaveCount(0);

  await openSettings(page);
  await page.getByTestId('provider-select').selectOption({ label: 'Balance Relay · ✓' });
  await page.getByTestId('provider-delete').click();
  await page.getByTestId('provider-delete-confirm').click();
  await page.getByTestId('settings-close').click();
});
