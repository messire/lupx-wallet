import { Page, expect } from '@playwright/test';

/** Dev-only password from backend/README.md / appsettings.Development.json — never used in production. */
export const DEV_PASSWORD = 'ChangeMe123!';

export async function login(page: Page): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('Пароль').fill(DEV_PASSWORD);
  await page.getByRole('button', { name: 'Войти' }).click();
  await expect(page).toHaveURL(/\/wallets$/);
}

/**
 * Creates a wallet type and currency via the Wallets create-form quick-add
 * rows if none exist yet, then fills and submits the form. Returns once the
 * new wallet card is visible. `name` should be unique per test to avoid
 * clashing with wallets left over from other scenarios sharing the same DB.
 */
export async function createWallet(page: Page, options: { name: string; initialBalance?: string }): Promise<void> {
  await page.getByRole('button', { name: 'Добавить кошелек' }).click();

  const typeSelect = page.locator('select[formcontrolname="walletTypeId"]');
  if ((await typeSelect.locator('option').count()) <= 1) {
    await page.getByRole('button', { name: '+ добавить тип' }).click();
    await page.getByPlaceholder('Название типа').fill('E2E тип кошелька');
    const typeQuickAdd = page.locator('.quick-add').filter({ has: page.getByPlaceholder('Название типа') });
    await typeQuickAdd.getByRole('button', { name: 'Добавить' }).click();
    await expect(typeSelect.locator('option')).not.toHaveCount(1);
  }
  await typeSelect.selectOption({ index: 1 });

  const currencySelect = page.locator('select[formcontrolname="currencyId"]');
  if ((await currencySelect.locator('option').count()) <= 1) {
    await page.getByRole('button', { name: '+ добавить валюту' }).click();
    const currencyCombobox = page.getByRole('combobox').last();
    await currencyCombobox.click();
    await currencyCombobox.fill('EUR');
    await page.getByRole('option', { name: /EUR/ }).first().click();
    await page.locator('.quick-add').getByRole('button', { name: 'Добавить' }).click();
    await expect(currencySelect.locator('option')).not.toHaveCount(1);
  }
  await currencySelect.selectOption({ index: 1 });

  await page.locator('input[formcontrolname="name"]').fill(options.name);
  if (options.initialBalance) {
    await page.locator('input[formcontrolname="initialBalanceAmount"]').fill(options.initialBalance);
  }

  await page.getByRole('button', { name: 'Создать', exact: true }).click();
  await expect(page.locator('.wallet-card', { hasText: options.name })).toBeVisible();
}
