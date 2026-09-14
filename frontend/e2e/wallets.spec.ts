import { test, expect } from '@playwright/test';
import { login, createWallet } from './fixtures';

test.beforeEach(async ({ page }) => {
  await login(page);
});

test('creates a wallet and shows a success confirmation', async ({ page }) => {
  const name = `E2E создание ${Date.now()}`;
  await createWallet(page, { name, initialBalance: '250' });

  await expect(page.getByText('Кошелек создан')).toBeVisible();
  await expect(page.locator('.wallet-card', { hasText: name })).toContainText('EUR');
});

test('shows a readable save error and keeps the entered data when the create request fails', async ({ page }) => {
  // Real backend/DB for everything else — only the one write that must fail is
  // intercepted, so the failure path is deterministic instead of depending on a
  // specific business-rule conflict.
  await page.route('**/api/v1/wallets', (route) => {
    if (route.request().method() === 'POST') {
      return route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        body: JSON.stringify({ title: 'Internal Server Error', status: 500, detail: 'Не удалось создать кошелек.' }),
      });
    }
    return route.continue();
  });

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
    const combobox = page.getByRole('combobox').last();
    await combobox.click();
    await combobox.fill('EUR');
    await page.getByRole('option', { name: /EUR/ }).first().click();
    await page.locator('.quick-add').getByRole('button', { name: 'Добавить' }).click();
    await expect(currencySelect.locator('option')).not.toHaveCount(1);
  }
  await currencySelect.selectOption({ index: 1 });

  const walletName = `E2E ошибка сохранения ${Date.now()}`;
  await page.locator('input[formcontrolname="name"]').fill(walletName);
  await page.getByRole('button', { name: 'Создать', exact: true }).click();

  await expect(page.getByText('Не удалось создать кошелек.')).toBeVisible();
  // The form stays open with the entered data — nothing is lost on a failed save.
  await expect(page.locator('input[formcontrolname="name"]')).toHaveValue(walletName);
  await expect(page.locator('.wallet-card', { hasText: walletName })).toHaveCount(0);
});

test('confirms deletion through the dialog and removes the wallet', async ({ page }) => {
  const name = `E2E удаление ${Date.now()}`;
  await createWallet(page, { name });

  const card = page.locator('.wallet-card', { hasText: name });
  await card.getByRole('button', { name: 'Удалить' }).click();

  const dialog = page.locator('dialog.app-dialog[open]');
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText(name);
  await expect(dialog).toContainText('без возможности восстановления');

  await dialog.getByRole('button', { name: 'Удалить', exact: true }).click();

  await expect(dialog).toBeHidden();
  await expect(page.getByText('Кошелек удалён')).toBeVisible();
  await expect(card).toHaveCount(0);
});

test('Escape closes the delete dialog without deleting and returns focus to the trigger button', async ({ page }) => {
  const name = `E2E escape ${Date.now()}`;
  await createWallet(page, { name });

  const card = page.locator('.wallet-card', { hasText: name });
  const deleteButton = card.getByRole('button', { name: 'Удалить' });
  await deleteButton.click();

  const dialog = page.locator('dialog.app-dialog[open]');
  await expect(dialog).toBeVisible();

  await page.keyboard.press('Escape');

  await expect(dialog).toBeHidden();
  // The wallet must still exist — Escape is a dismissal, not a confirmation.
  await expect(card).toBeVisible();
  await expect(deleteButton).toBeFocused();
});
