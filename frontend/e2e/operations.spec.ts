import { test, expect, login, createWallet, ensureOperationType, isoDateDaysAgo } from './fixtures';

test.beforeEach(async ({ page }) => {
  await login(page);
});

test('creates an operation through the dialog and shows a success confirmation', async ({ page }) => {
  const walletName = `E2E операции ${Date.now()}`;
  await createWallet(page, { name: walletName });
  await ensureOperationType(page, 'E2E расход', 'Расход');

  await page.goto('/operations');
  await page.locator('.page-heading').getByRole('button', { name: 'Добавить операцию' }).click();

  const dialog = page.getByRole('dialog', { name: 'Новая операция' });
  await expect(dialog).toBeVisible();

  await dialog.locator('select[formcontrolname="walletId"]').selectOption({ label: walletName });
  await dialog.locator('select[formcontrolname="operationTypeId"]').selectOption({ label: 'E2E расход' });
  await dialog.locator('input[formcontrolname="amount"]').fill('12.50');
  await dialog.getByRole('button', { name: 'Создать', exact: true }).click();

  await expect(dialog).toBeHidden();
  await expect(page.getByText('Операция создана')).toBeVisible();
  await expect(page.locator('tbody tr', { hasText: walletName })).toBeVisible();
});

test('Escape closes the operation dialog without creating an operation', async ({ page }) => {
  await page.goto('/operations');
  await page.locator('.page-heading').getByRole('button', { name: 'Добавить операцию' }).click();

  const dialog = page.getByRole('dialog', { name: 'Новая операция' });
  await expect(dialog).toBeVisible();

  await page.keyboard.press('Escape');

  await expect(dialog).toBeHidden();
  await expect(page.locator('.page-heading').getByRole('button', { name: 'Добавить операцию' })).toBeFocused();
});

test('soft-deletes a past-dated operation: the row stays, struck through with a "Удалена" badge, and can be restored', async ({ page }) => {
  const walletName = `E2E удаление операции ${Date.now()}`;
  await createWallet(page, { name: walletName, accountingStartDate: isoDateDaysAgo(30) });
  await ensureOperationType(page, 'E2E расход', 'Расход');

  await page.goto('/operations');
  await page.locator('.page-heading').getByRole('button', { name: 'Добавить операцию' }).click();
  const createDialog = page.getByRole('dialog', { name: 'Новая операция' });
  await createDialog.locator('select[formcontrolname="walletId"]').selectOption({ label: walletName });
  await createDialog.locator('select[formcontrolname="operationTypeId"]').selectOption({ label: 'E2E расход' });
  await createDialog.locator('input[formcontrolname="amount"]').fill('5.00');
  // ADR-0002: a past date must not prevent deletion (the old "today only" rule is gone).
  await createDialog.locator('input[formcontrolname="operationDate"]').fill(isoDateDaysAgo(10));
  await createDialog.getByRole('button', { name: 'Создать', exact: true }).click();
  await expect(createDialog).toBeHidden();

  const row = page.locator('tbody tr', { hasText: walletName });
  await expect(row).toBeVisible();
  await expect(row).not.toHaveClass(/is-deleted/);

  // Delete through "···" -> confirmation (does not promise irreversibility).
  await row.getByRole('button', { name: 'Действия с операцией' }).click();
  const rowActionsDialog = page.getByRole('dialog', { name: 'Действия с операцией' });
  await expect(rowActionsDialog).toBeVisible();
  await expect(rowActionsDialog.getByRole('button', { name: 'Восстановить', exact: true })).toHaveCount(0);
  await rowActionsDialog.getByRole('button', { name: 'Удалить', exact: true }).click();

  const confirmDialog = page.getByRole('dialog', { name: 'Удалить операцию' });
  await expect(confirmDialog).toBeVisible();
  await expect(confirmDialog).toContainText('Операцию можно восстановить');
  await expect(confirmDialog).not.toContainText('без возможности восстановления');
  await expect(confirmDialog).not.toContainText('уже учтена в истории баланса');
  await confirmDialog.getByRole('button', { name: 'Удалить', exact: true }).click();

  // The deleted row stays in the list: grey, struck through in red, with a text badge.
  await expect(confirmDialog).toBeHidden();
  await expect(page.getByText('Операция удалена')).toBeVisible();
  await expect(row).toHaveCount(1);
  await expect(row).toHaveClass(/is-deleted/);
  await expect(row.locator('app-badge', { hasText: 'Удалена' })).toBeVisible();
  await expect(row.locator('td').first()).toHaveCSS('text-decoration-line', 'line-through');
  await expect(row.locator('td.actions')).not.toHaveCSS('text-decoration-line', 'line-through');

  // In the "···" menu of a deleted operation only "Восстановить" is offered.
  await row.getByRole('button', { name: 'Действия с операцией' }).click();
  await expect(rowActionsDialog).toBeVisible();
  await expect(rowActionsDialog.getByRole('button', { name: 'Изменить', exact: true })).toHaveCount(0);
  await expect(rowActionsDialog.getByRole('button', { name: 'Удалить', exact: true })).toHaveCount(0);
  await rowActionsDialog.getByRole('button', { name: 'Восстановить', exact: true }).click();

  // Restoring needs no confirmation: the badge disappears and a toast is shown.
  await expect(page.getByText('Операция восстановлена')).toBeVisible();
  await expect(row).toHaveCount(1);
  await expect(row).not.toHaveClass(/is-deleted/);
  await expect(row.locator('app-badge', { hasText: 'Удалена' })).toHaveCount(0);
});
