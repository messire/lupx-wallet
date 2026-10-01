import { test, expect, login, createWallet } from './fixtures';

// Known rate limit (see docs/PROGRESS.md): login is limited to 5 requests/minute,
// and each test logs in via beforeEach — keep this file to a small number of
// scenarios (3) so the whole file can be run together without hitting 429s.
test.beforeEach(async ({ page }) => {
  await login(page);
});

test('selecting a wallet and showing balance with an empty date displays the real current balance', async ({ page }) => {
  const walletName = `E2E баланс на дату ${Date.now()}`;
  await createWallet(page, { name: walletName, initialBalance: '250' });

  await page.goto('/balance-history');
  await page.getByLabel('Кошелек', { exact: true }).selectOption({ label: walletName });

  // Date field left empty — "Показать" (not "Показать историю") must return the
  // real current balance from the API, never a client-side default of 0.
  await page.getByRole('button', { name: 'Показать', exact: true }).click();

  const balance = page.locator('.balance-result .balance');
  await expect(balance).not.toHaveText('—');
  await expect(page.locator('.balance-result')).toContainText('250');
});

test('a reverse date range for the history shows a validation error without calling the API', async ({ page }) => {
  const walletName = `E2E обратный диапазон ${Date.now()}`;
  await createWallet(page, { name: walletName });

  await page.goto('/balance-history');
  await page.getByLabel('Кошелек', { exact: true }).selectOption({ label: walletName });

  let historyRequests = 0;
  await page.route('**/balance-history**', (route) => {
    historyRequests += 1;
    return route.continue();
  });

  await page.locator('input[formcontrolname="from"]').fill('2026-06-01');
  await page.locator('input[formcontrolname="to"]').fill('2026-01-01');
  await page.getByRole('button', { name: 'Показать историю' }).click();

  await expect(page.getByRole('alert')).toContainText('не может быть позже');
  expect(historyRequests).toBe(0);
});

test('changing the wallet after a result is shown clears both results and both sections’ dates', async ({ page }) => {
  const walletName = `E2E смена кошелька A ${Date.now()}`;
  const otherWalletName = `E2E смена кошелька B ${Date.now()}`;
  await createWallet(page, { name: walletName, initialBalance: '100' });
  await createWallet(page, { name: otherWalletName, initialBalance: '200' });

  await page.goto('/balance-history');
  await page.getByLabel('Кошелек', { exact: true }).selectOption({ label: walletName });

  // Balance-on-date section: produce a shown result.
  await page.getByRole('button', { name: 'Показать', exact: true }).click();
  await expect(page.locator('.balance-result .balance')).not.toHaveText('—');

  // History section: produce a shown (applied) period caption.
  await page.locator('input[formcontrolname="from"]').fill('2026-01-01');
  await page.locator('input[formcontrolname="to"]').fill('2026-01-31');
  await page.getByRole('button', { name: 'Показать историю' }).click();
  await expect(page.locator('.history-period')).toBeVisible();

  await page.getByLabel('Кошелек', { exact: true }).selectOption({ label: otherWalletName });

  // Both results are cleared…
  await expect(page.locator('.balance-result .balance')).toHaveText('—');
  await expect(page.locator('.history-period')).toHaveCount(0);
  // …and both sections' dates are cleared, not just their results.
  await expect(page.locator('input[type="date"]').first()).toHaveValue('');
  await expect(page.locator('input[formcontrolname="from"]')).toHaveValue('');
  await expect(page.locator('input[formcontrolname="to"]')).toHaveValue('');
});
