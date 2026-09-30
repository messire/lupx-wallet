import { Page } from '@playwright/test';
import { test, expect, login, createWallet, ensureTransferOperationType, isoDateDaysAgo } from './fixtures';

/**
 * Creates two wallets that end up sharing the same currency — `createWallet`
 * (fixtures.ts) always selects the *first* currency option (quick-adding EUR
 * only if the list is still empty), so calling it twice in a row with nothing
 * else touching the currency list in between yields two same-currency wallets,
 * without duplicating its quick-add flow here.
 */
async function createTwoSameCurrencyWallets(
  page: Page,
  prefix: string,
  accountingStartDate?: string,
): Promise<{ source: string; target: string }> {
  const source = `${prefix} источник ${Date.now()}`;
  const target = `${prefix} получатель ${Date.now()}`;
  await createWallet(page, { name: source, initialBalance: '500', accountingStartDate });
  await createWallet(page, { name: target, initialBalance: '0', accountingStartDate });
  // Creating a transfer needs an active operation type with behavior "Transfer" (absent in a fresh database).
  await ensureTransferOperationType(page);
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

test('soft-deletes a past-dated transfer: the row stays, struck through with a "Удалён" badge, and can be restored', async ({ page }) => {
  const { source, target } = await createTwoSameCurrencyWallets(page, 'E2E удаление перевода', isoDateDaysAgo(30));

  await page.goto('/transfers');
  await page.locator('.page-heading').getByRole('button', { name: 'Добавить перевод' }).click();
  const createDialog = page.getByRole('dialog', { name: 'Новый перевод' });
  await createDialog.locator('select[formcontrolname="sourceWalletId"]').selectOption({ label: source });
  await createDialog.locator('select[formcontrolname="targetWalletId"]').selectOption({ label: target });
  await createDialog.locator('input[formcontrolname="amount"]').fill('15.00');
  // ADR-0002: a past date must not prevent deletion (the old "today only" rule is gone).
  await createDialog.locator('input[formcontrolname="transferDate"]').fill(isoDateDaysAgo(10));
  await createDialog.getByRole('button', { name: 'Создать', exact: true }).click();
  await expect(createDialog).toBeHidden();

  const row = page.locator('tbody tr', { hasText: source }).filter({ hasText: target });
  await expect(row).toBeVisible();
  await expect(row).not.toHaveClass(/is-deleted/);

  await row.getByRole('button', { name: 'Действия с переводом' }).click();
  const rowActionsDialog = page.getByRole('dialog', { name: 'Действия с переводом' });
  await expect(rowActionsDialog).toBeVisible();
  await expect(rowActionsDialog.getByRole('button', { name: 'Восстановить перевод', exact: true })).toHaveCount(0);
  await rowActionsDialog.getByRole('button', { name: 'Удалить перевод', exact: true }).click();

  const confirmDialog = page.getByRole('dialog', { name: 'Удалить перевод?' });
  await expect(confirmDialog).toBeVisible();
  await expect(confirmDialog).toContainText(source);
  await expect(confirmDialog).toContainText(target);
  await expect(confirmDialog).toContainText('её можно восстановить');
  await expect(confirmDialog).not.toContainText('без возможности восстановления');
  await confirmDialog.getByRole('button', { name: 'Удалить', exact: true }).click();

  // The deleted row stays in the list: grey, struck through in red, with a text badge.
  await expect(confirmDialog).toBeHidden();
  await expect(page.getByText('Перевод удалён', { exact: true })).toBeVisible();
  await expect(row).toHaveCount(1);
  await expect(row).toHaveClass(/is-deleted/);
  await expect(row.locator('app-badge', { hasText: 'Удалён' })).toBeVisible();
  await expect(row.locator('td').first()).toHaveCSS('text-decoration-line', 'line-through');

  // In the "···" menu of a deleted transfer only "Восстановить перевод" is offered.
  await row.getByRole('button', { name: 'Действия с переводом' }).click();
  await expect(rowActionsDialog).toBeVisible();
  await expect(rowActionsDialog.getByRole('button', { name: 'Удалить перевод', exact: true })).toHaveCount(0);
  await rowActionsDialog.getByRole('button', { name: 'Восстановить перевод', exact: true }).click();

  // Restoring needs no confirmation: the badge disappears and a toast is shown.
  await expect(page.getByText('Перевод восстановлен')).toBeVisible();
  await expect(row).toHaveCount(1);
  await expect(row).not.toHaveClass(/is-deleted/);
  await expect(row.locator('app-badge', { hasText: 'Удалён' })).toHaveCount(0);
});
