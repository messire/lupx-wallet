import { Page } from '@playwright/test';
import { test, expect, login, createWallet } from './fixtures';

/**
 * Creates two wallets that end up sharing the same currency — `createWallet`
 * (fixtures.ts) always selects the *first* currency option (quick-adding EUR
 * only if the list is still empty), so calling it twice in a row with nothing
 * else touching the currency list in between yields two same-currency wallets,
 * without duplicating its quick-add flow here.
 */
async function createTwoSameCurrencyWallets(page: Page, prefix: string): Promise<{ source: string; target: string }> {
  const source = `${prefix} источник ${Date.now()}`;
  const target = `${prefix} получатель ${Date.now()}`;
  await createWallet(page, { name: source, initialBalance: '500' });
  await createWallet(page, { name: target, initialBalance: '0' });
  return { source, target };
}

test.beforeEach(async ({ page }) => {
  await login(page);
});

test('creates a transfer between two same-currency wallets through the dialog', async ({ page }) => {
  const { source, target } = await createTwoSameCurrencyWallets(page, 'E2E перевод');

  await page.goto('/transfers');
  await page.locator('.page-heading').getByRole('button', { name: 'Добавить перевод' }).click();

  const dialog = page.getByRole('dialog', { name: 'Новый перевод' });
  await expect(dialog).toBeVisible();

  await dialog.locator('select[formcontrolname="sourceWalletId"]').selectOption({ label: source });
  await dialog.locator('select[formcontrolname="targetWalletId"]').selectOption({ label: target });
  await dialog.locator('input[formcontrolname="amount"]').fill('50.00');
  await dialog.getByRole('button', { name: 'Создать', exact: true }).click();

  await expect(dialog).toBeHidden();
  await expect(page.getByText('Перевод создан')).toBeVisible();

  const row = page.locator('tbody tr', { hasText: source }).filter({ hasText: target });
  await expect(row).toBeVisible();
  await expect(row).toContainText('50');
});

test('Escape on a modified transfer form asks for confirmation, and "Продолжить заполнение" keeps the entered input', async ({ page }) => {
  await page.goto('/transfers');
  await page.locator('.page-heading').getByRole('button', { name: 'Добавить перевод' }).click();

  const dialog = page.getByRole('dialog', { name: 'Новый перевод' });
  await expect(dialog).toBeVisible();

  await dialog.locator('input[formcontrolname="amount"]').fill('42.50');

  await page.keyboard.press('Escape');

  const confirmDialog = page.getByRole('dialog', { name: 'Отменить изменения?' });
  await expect(confirmDialog).toBeVisible();

  await confirmDialog.getByRole('button', { name: 'Продолжить заполнение' }).click();

  await expect(confirmDialog).toBeHidden();
  await expect(dialog).toBeVisible();
  await expect(dialog.locator('input[formcontrolname="amount"]')).toHaveValue('42.50');
});

test('deletes a today-dated transfer through "···" and removes it from the table', async ({ page }) => {
  const { source, target } = await createTwoSameCurrencyWallets(page, 'E2E удаление перевода');

  await page.goto('/transfers');
  await page.locator('.page-heading').getByRole('button', { name: 'Добавить перевод' }).click();
  const createDialog = page.getByRole('dialog', { name: 'Новый перевод' });
  await createDialog.locator('select[formcontrolname="sourceWalletId"]').selectOption({ label: source });
  await createDialog.locator('select[formcontrolname="targetWalletId"]').selectOption({ label: target });
  await createDialog.locator('input[formcontrolname="amount"]').fill('15.00');
  // transferDate defaults to today (required by the "no deleting a past-dated
  // transfer" rule this test exercises) — left untouched.
  await createDialog.getByRole('button', { name: 'Создать', exact: true }).click();
  await expect(createDialog).toBeHidden();

  const row = page.locator('tbody tr', { hasText: source }).filter({ hasText: target });
  await expect(row).toBeVisible();
  await row.getByRole('button', { name: 'Действия с переводом' }).click();

  const rowActionsDialog = page.getByRole('dialog', { name: 'Действия с переводом' });
  await expect(rowActionsDialog).toBeVisible();
  await rowActionsDialog.getByRole('button', { name: 'Удалить перевод', exact: true }).click();

  const confirmDialog = page.getByRole('dialog', { name: 'Удалить перевод?' });
  await expect(confirmDialog).toBeVisible();
  await expect(confirmDialog).toContainText(source);
  await expect(confirmDialog).toContainText(target);
  await expect(confirmDialog).toContainText('без возможности восстановления');

  await confirmDialog.getByRole('button', { name: 'Удалить', exact: true }).click();

  await expect(confirmDialog).toBeHidden();
  await expect(page.getByText('Перевод удалён')).toBeVisible();
  await expect(row).toHaveCount(0);
});
