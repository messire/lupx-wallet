import {
  test,
  expect,
  login,
  createWallet,
  apiRequest,
  ensureAvailableCurrencyOnPage,
  removeCurrencyIfActive,
  selectWalletCurrency,
  trackCurrencyForCleanup,
} from './fixtures';

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

  // The currency comes from the list of available currencies, filled only by a rates refresh (ADR-0004 §7).
  await ensureAvailableCurrencyOnPage(page, 'EUR');
  await page.getByRole('button', { name: 'Добавить кошелек' }).first().click();

  const typeSelect = page.locator('select[formcontrolname="walletTypeId"]');
  await expect(typeSelect).toBeEnabled();
  if ((await typeSelect.locator('option').count()) <= 1) {
    await page.getByRole('button', { name: '+ добавить тип' }).click();
    await page.getByPlaceholder('Название типа').fill('E2E тип кошелька');
    const typeQuickAdd = page.locator('.quick-add').filter({ has: page.getByPlaceholder('Название типа') });
    await typeQuickAdd.getByRole('button', { name: 'Добавить' }).click();
    await expect(typeSelect.locator('option')).not.toHaveCount(1);
  }
  await typeSelect.selectOption({ index: 1 });

  await selectWalletCurrency(page, 'EUR');

  const walletName = `E2E ошибка сохранения ${Date.now()}`;
  await page.locator('input[formcontrolname="name"]').fill(walletName);
  await page.getByRole('button', { name: 'Создать', exact: true }).click();

  await expect(page.getByText('Не удалось создать кошелек.')).toBeVisible();
  // The form stays open with the entered data — nothing is lost on a failed save.
  await expect(page.locator('input[formcontrolname="name"]')).toHaveValue(walletName);
  await expect(page.locator('.wallet-card', { hasText: walletName })).toHaveCount(0);
});

test('a wallet in a currency that is not active yet activates it: the currency shows up among the active ones', async ({
  page,
}) => {
  // JPY is used by this scenario only. Start from "not active" so the activation is really caused by the
  // wallet; a previous run's leftover (archived wallet still using it => 409) would make the check vacuous.
  const code = 'JPY';
  trackCurrencyForCleanup(page, code);
  const removed = await removeCurrencyIfActive(page, code);
  test.skip(removed === 409, `${code} is still used by a wallet or an operation from an earlier run — cannot start from "not active"`);

  const name = `E2E новая валюта ${Date.now()}`;
  await createWallet(page, { name, currencyCode: code });
  await expect(page.locator('.wallet-card', { hasText: name })).toContainText(code);

  // The server activated it in the same transaction as the wallet (no "add currency" step in between).
  const { body } = await apiRequest<{ data: Array<{ code: string }> }>(page, 'GET', '/currencies?limit=100');
  expect(body?.data.map((currency) => currency.code)).toContain(code);

  await page.goto('/reference-data');
  await page.getByRole('tab', { name: 'Валюты' }).click();
  const panel = page.locator('.tab-panel:not([hidden])');
  await expect(panel.locator('.ref-table tr', { hasText: code })).toBeVisible();
  // ... and it is no longer offered for adding: it is active.
  const combobox = panel.getByRole('combobox', { name: 'Добавить в активные' });
  await combobox.click();
  await combobox.fill(code);
  await expect(panel.getByRole('option', { name: new RegExp(`^${code} —`) })).toHaveCount(0);
});

test('confirms deletion through the dialog and removes the wallet', async ({ page }) => {
  const name = `E2E удаление ${Date.now()}`;
  await createWallet(page, { name });

  // "Удалить" lives in the card's "···" menu popover (not on the card itself).
  const card = page.locator('.wallet-card', { hasText: name });
  await card.getByRole('button', { name: `Действия: ${name}` }).click();
  await page.locator('.wallet-card__menu-popover').getByRole('button', { name: 'Удалить' }).click();

  const dialog = page.locator('dialog.app-dialog[open]');
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText(name);
  await expect(dialog).toContainText('без возможности восстановления');

  await dialog.getByRole('button', { name: 'Удалить', exact: true }).click();

  await expect(dialog).toBeHidden();
  await expect(page.getByText('Кошелек удалён')).toBeVisible();
  await expect(card).toHaveCount(0);
});

test('Escape closes the delete dialog without deleting and returns focus to the menu trigger', async ({ page }) => {
  const name = `E2E escape ${Date.now()}`;
  await createWallet(page, { name });

  const card = page.locator('.wallet-card', { hasText: name });
  const menuTrigger = card.getByRole('button', { name: `Действия: ${name}` });
  await menuTrigger.click();
  await page.locator('.wallet-card__menu-popover').getByRole('button', { name: 'Удалить' }).click();

  const dialog = page.locator('dialog.app-dialog[open]');
  await expect(dialog).toBeVisible();

  await page.keyboard.press('Escape');

  await expect(dialog).toBeHidden();
  // The wallet must still exist — Escape is a dismissal, not a confirmation.
  await expect(card).toBeVisible();
  await expect(menuTrigger).toBeFocused();
});
