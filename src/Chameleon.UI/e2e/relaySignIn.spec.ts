import { expect, test, type Page } from '@playwright/test';
import { expectGenerated, openApp } from './helpers';

/** Accounts of the development host's fake relay (tools/Chameleon.DevHost/FakeRelayAccounts.cs). */
const PASSWORD = 'dev-password';
const TWO_FACTOR_CODE = '123456';

async function fillCredentials(page: Page, username: string, password: string): Promise<void> {
  await page.getByTestId('signin-username').fill(username);
  await page.getByTestId('signin-password').fill(password);
  await page.getByTestId('signin-submit').click();
}

test('signs in to the relay from the main panel, generates, and signs out', async ({ page }) => {
  await openApp(page);
  await page.getByTestId('backend-select').selectOption('direct:Chameleon');
  await page.getByTestId('backend-signin').click();
  const dialog = page.getByTestId('relay-signin-dialog');
  await expect(dialog).toBeVisible();

  await fillCredentials(page, 'dev', 'wrong-password');
  await expect(page.getByTestId('signin-error')).toContainText('Sign-in failed');
  await expect(page.getByTestId('signin-error')).toContainText('Username or password is incorrect');

  await page.getByTestId('signin-password').fill(PASSWORD);
  await page.getByTestId('signin-submit').click();
  await expect(dialog).toBeHidden();
  await expect(page.getByTestId('backend-not-ready')).toHaveCount(0);
  await expect(page.getByTestId('signed-in-as')).toHaveText('Account: dev');
  await expect(page.getByTestId('balance-text')).toContainText('Balance');

  await page.getByTestId('capture-button').click();
  await page.getByTestId('prompt-input').fill('render through the signed-in account');
  await page.getByTestId('generate-button').click();
  await expectGenerated(page);

  await page.getByTestId('signin-signout').click();
  await expect(page.getByTestId('backend-signin')).toBeVisible();
  await expect(page.getByTestId('signed-in-as')).toHaveCount(0);
});

test('signs in with a two-factor code from the settings', async ({ page }) => {
  await openApp(page);
  await page.getByTestId('open-settings').click();
  await page.getByTestId('provider-select').selectOption('Chameleon');
  await page.getByTestId('provider-signin').click();
  await expect(page.getByTestId('settings-dialog')).toHaveCount(0);

  await fillCredentials(page, 'dev-2fa', PASSWORD);
  await page.getByTestId('signin-code').fill('000000');
  await page.getByTestId('signin-verify').click();
  await expect(page.getByTestId('signin-error')).toBeVisible();
  await page.getByTestId('signin-code').fill(TWO_FACTOR_CODE);
  await page.getByTestId('signin-verify').click();
  await expect(page.getByTestId('relay-signin-dialog')).toBeHidden();

  await page.getByTestId('open-settings').click();
  await page.getByTestId('provider-select').selectOption('Chameleon');
  await expect(page.getByTestId('provider-signed-in-as')).toHaveText('Signed in as dev-2fa');
  await expect(page.getByTestId('key-input')).toHaveAttribute('placeholder', 'Saved — type to replace');

  await page.getByTestId('provider-signout').click();
  await expect(page.getByTestId('provider-signin')).toBeVisible();
  await expect(page.getByTestId('key-clear')).toHaveCount(0);
  await page.getByTestId('settings-close').click();
});

test('explains the sign-in rate limit of the website instead of blaming the balance', async ({ page }) => {
  await openApp(page);
  await page.getByTestId('backend-select').selectOption('direct:Chameleon');
  await page.getByTestId('backend-signin').click();

  await fillCredentials(page, 'dev-limited', PASSWORD);

  await expect(page.getByTestId('signin-error')).toContainText('Too many sign-in attempts');
  await page.getByTestId('relay-signin-dialog').getByRole('button', { name: 'Close' }).click();
});
