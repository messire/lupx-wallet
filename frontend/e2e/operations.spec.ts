import { test, expect, login, createWallet, ensureOperationType } from './fixtures';

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

test('confirms deletion through the dialog and removes the operation', async ({ page }) => {
  const walletName = `E2E удаление операции ${Date.now()}`;
  await createWallet(page, { name: walletName });
  await ensureOperationType(page, 'E2E расход', 'Расход');

  await page.goto('/operations');
  await page.locator('.page-heading').getByRole('button', { name: 'Добавить операцию' }).click();
  const createDialog = page.getByRole('dialog', { name: 'Новая операция' });
  await createDialog.locator('select[formcontrolname="walletId"]').selectOption({ label: walletName });
  await createDialog.locator('select[formcontrolname="operationTypeId"]').selectOption({ label: 'E2E расход' });
  await createDialog.locator('input[formcontrolname="amount"]').fill('5.00');
  await createDialog.getByRole('button', { name: 'Создать', exact: true }).click();
  await expect(createDialog).toBeHidden();

  const row = page.locator('tbody tr', { hasText: walletName });
  await row.getByRole('button', { name: 'Действия с операцией' }).click();

  const rowActionsDialog = page.getByRole('dialog', { name: 'Действия с операцией' });
  await expect(rowActionsDialog).toBeVisible();
  await rowActionsDialog.getByRole('button', { name: 'Удалить', exact: true }).click();

  const confirmDialog = page.getByRole('dialog', { name: 'Удалить операцию' });
  await expect(confirmDialog).toBeVisible();
  await expect(confirmDialog).toContainText('без возможности восстановления');

  await confirmDialog.getByRole('button', { name: 'Удалить', exact: true }).click();

  await expect(confirmDialog).toBeHidden();
  await expect(page.getByText('Операция удалена')).toBeVisible();
  await expect(row).toHaveCount(0);
});
