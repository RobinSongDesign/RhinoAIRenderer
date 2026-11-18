import { expect, test, type Page } from '@playwright/test';
import { expectCaptured, expectGenerated, openApp, rpc } from './helpers';

/**
 * Account mode against the development host's fake gateway (--fake-cloud): the user starts with
 * 10 credits, sign-ins are approved after 500 ms, 1K costs 2 credits and 2K costs 4, and the code
 * TEST-TEST-TEST-TEST adds 100 credits.
 * The whole journey is one test, because each step depends on the account state of the previous one.
 */

async function generate(page: Page): Promise<void> {
  await page.getByTestId('capture-button').click();
  await expectCaptured(page);
  await page.getByTestId('prompt-input').fill('cloud render');
  await page.getByTestId('generate-button').click();
  await expect(page.getByTestId('cancel-button')).toBeHidden();
  await expectGenerated(page);
}

test('signs in, spends and redeems credits, then signs out and returns to own keys', async ({ page }) => {
  await openApp(page);
  const before = await rpc<{ activeBackendId: string }>(page, 'app.getState');

  // First run: no key and not signed in, so the getting-started card offers both ways.
  const welcome = page.getByTestId('welcome-card');
  await expect(welcome).toBeVisible();
  await welcome.getByTestId('welcome-account').click();

  const login = page.getByTestId('login-dialog');
  await expect(login.getByTestId('login-code')).toHaveText('DEVC-ODE1');
  await expect(login).toBeHidden();

  // Signed in: the balance is in the header, the account backend is active and ready.
  await expect(page.getByTestId('account-credits')).toHaveText('10 credits');
  await expect(page.getByTestId('backend-select')).toHaveValue('cloud');
  await expect(page.getByTestId('backend-not-ready')).toBeHidden();
  await expect(welcome).toBeHidden();
  // Templates come from the gateway's catalog in account mode.
  await expect(page.getByTestId('style-select').locator('option[value="cloud-night"]')).toHaveCount(1);

  // A 1K generation costs 2 credits.
  await page.getByTestId('size-select').selectOption('1K');
  await expect(page.getByTestId('cost-hint')).toHaveText('Costs 2 credits');
  await generate(page);
  await expect(page.getByTestId('account-credits')).toHaveText('8 credits');

  // Two 2K generations use up the rest; then the button is disabled with a hint to redeem.
  await page.getByTestId('size-select').selectOption('2K');
  await expect(page.getByTestId('cost-hint')).toHaveText('Costs 4 credits');
  await page.getByTestId('generate-button').click();
  await expect(page.getByTestId('account-credits')).toHaveText('4 credits');
  await page.getByTestId('generate-button').click();
  await expect(page.getByTestId('account-credits')).toHaveText('0 credits');
  await expect(page.getByTestId('cost-hint')).toHaveAttribute('data-short', 'true');
  await expect(page.getByTestId('generate-button')).toBeDisabled();

  // Redeem: a wrong code is explained, the test code adds 100 credits.
  await page.getByTestId('cost-top-up').click();
  const redeem = page.getByTestId('redeem-dialog');
  await redeem.getByTestId('redeem-input').fill('nope nope nope nope');
  await expect(redeem.getByTestId('redeem-input')).toHaveValue('NOPE-NOPE-NOPE-NOPE');
  await redeem.getByTestId('redeem-submit').click();
  await expect(redeem.getByTestId('redeem-outcome')).toHaveAttribute('data-kind', 'error');
  await redeem.getByTestId('redeem-input').fill('testtesttesttest');
  await redeem.getByTestId('redeem-submit').click();
  await expect(redeem.getByTestId('redeem-outcome')).toHaveText('Added 100 credits; balance 100 credits');
  await expect(page.getByTestId('account-credits')).toHaveText('100 credits');
  await redeem.getByRole('button', { name: 'Close' }).first().click();
  await expect(page.getByTestId('generate-button')).toBeEnabled();

  // The credit history lists the redemption and the generations.
  await page.getByTestId('account-button').click();
  await page.getByTestId('account-ledger').click();
  const ledger = page.getByTestId('ledger-dialog');
  await expect(ledger.locator('tr')).not.toHaveCount(0);
  await expect(ledger).toContainText('Redeemed');
  await ledger.getByRole('button', { name: 'Close' }).first().click();

  // Running out of credits during a job (simulated by the fake gateway) opens the redeem dialog.
  await page.getByTestId('prompt-input').fill('render #fail:insufficient_credits');
  await page.getByTestId('generate-button').click();
  await expect(page.getByTestId('redeem-dialog')).toBeVisible();
  await expect(page.getByTestId('redeem-dialog')).toContainText('Not enough credits');
  await page.getByTestId('redeem-dialog').getByRole('button', { name: 'Close' }).first().click();

  // Sign out: the account backend is no longer ready.
  await page.getByTestId('account-button').click();
  await page.getByTestId('account-sign-out').click();
  await expect(page.getByTestId('account-sign-in')).toBeVisible();
  await expect(page.getByTestId('backend-not-ready')).toContainText('Sign in');

  // Switch to own keys in the settings: provider settings appear again.
  await page.getByTestId('open-settings').click();
  const settings = page.getByTestId('settings-dialog');
  await expect(settings.getByTestId('provider-select')).toBeHidden();
  await settings.getByTestId('access-mode').selectOption('ownKey');
  await expect(settings.getByTestId('provider-select')).toBeVisible();
  await settings.getByTestId('settings-close').click();
  await expect(page.getByTestId('backend-select')).not.toHaveValue('cloud');

  // Leave the host as other tests expect it.
  await rpc(page, 'backend.select', { backendId: before.activeBackendId });
});
