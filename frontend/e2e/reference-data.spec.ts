import { test, expect, login, ensureAvailableCurrency, removeCurrencyIfActive, trackCurrencyForCleanup } from './fixtures';

// Currencies tab of the reference-data screen (ADR-0004 §7, UC-28): a currency is added to the active ones
// from the list of available currencies and removed from them again — there is no deactivation. The
// available list is filled by a rates refresh from the real Frankfurter service (network required).

test.beforeEach(async ({ page }) => {
  await login(page);
});

test('adds a currency from the available ones and removes it from the active ones again', async ({ page }) => {
  // CHF is used by this scenario only: nothing else creates a wallet in it, so it can always be removed again.
  const code = 'CHF';
  trackCurrencyForCleanup(page, code);
  await ensureAvailableCurrency(page, code);
  const leftover = await removeCurrencyIfActive(page, code);
  expect(leftover, `${code} is still used by a wallet or an operation — the scenario cannot start from "not active"`).not.toBe(409);

  await page.goto('/reference-data');
  await page.getByRole('tab', { name: 'Валюты' }).click();
  const panel = page.locator('.tab-panel:not([hidden])');
  const row = panel.locator('.ref-table tr', { hasText: code });
  await expect(row).toHaveCount(0);

  // Add: the combobox offers the available currencies that are not active yet.
  const combobox = panel.getByRole('combobox', { name: 'Добавить в активные' });
  await combobox.click();
  await combobox.fill(code);
  await panel.getByRole('option', { name: new RegExp(`^${code} —`) }).click();
  await panel.getByRole('button', { name: 'Добавить', exact: true }).click();

  // The name comes from the service (English), not typed by the user.
  await expect(row).toBeVisible();
  await expect(row).toContainText('Swiss Franc');
  await expect(row.getByRole('button', { name: 'Убрать из активных' })).toBeVisible();
  await expect(panel.getByText('Деактивировать')).toHaveCount(0);

  // It is active now, so it is no longer offered for adding.
  await combobox.click();
  await combobox.fill(code);
  await expect(panel.getByRole('option', { name: new RegExp(`^${code} —`) })).toHaveCount(0);
  await combobox.press('Escape');

  // Remove: a confirmation first, nothing happens until it is confirmed.
  await row.getByRole('button', { name: 'Убрать из активных' }).click();
  const dialog = page.locator('dialog.app-dialog[open]');
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText(code);
  await dialog.getByRole('button', { name: 'Отмена' }).click();
  await expect(dialog).toBeHidden();
  await expect(row).toBeVisible();

  await row.getByRole('button', { name: 'Убрать из активных' }).click();
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Убрать', exact: true }).click();
  await expect(dialog).toBeHidden();
  await expect(row).toHaveCount(0);

  // It stays among the available currencies and can be added again.
  await combobox.click();
  await combobox.fill(code);
  await expect(panel.getByRole('option', { name: new RegExp(`^${code} —`) })).toBeVisible();
});
