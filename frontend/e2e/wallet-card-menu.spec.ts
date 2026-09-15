import { Locator } from '@playwright/test';
import { test, expect, login, createWallet } from './fixtures';

test.beforeEach(async ({ page }) => {
  await login(page);
});

/**
 * The shared dev DB accumulates wallets across E2E runs (own known limitation,
 * see docs/PROGRESS.md), so the grid can be tall enough that Playwright's
 * built-in auto-scroll-then-click races the page's own scroll/reflow and the
 * click lands before the target has actually settled at its post-scroll
 * position — confirmed by comparing a plain `.click()` (flaky here) against
 * `.scrollIntoViewIfNeeded()` + a settle wait + `.click()` (reliable). Not an
 * app bug: a manual click in a real browser and `el.click()` via
 * `page.evaluate` both toggle the menu correctly regardless of list length.
 */
async function clickTrigger(trigger: Locator): Promise<void> {
  await trigger.scrollIntoViewIfNeeded();
  await trigger.page().waitForTimeout(150);
  await trigger.click();
}

test('opens via mouse click, closes via Escape, and returns focus to the trigger', async ({ page }) => {
  const name = `E2E меню ${Date.now()}`;
  await createWallet(page, { name });

  const card = page.locator('.wallet-card', { hasText: name });
  const trigger = card.getByRole('button', { name: `Действия: ${name}` });
  await clickTrigger(trigger);

  const popover = page.locator('.wallet-card__menu-popover');
  await expect(popover).toBeVisible();
  await expect(popover.getByRole('button', { name: 'Изменить' })).toBeVisible();
  await expect(popover.getByRole('button', { name: 'Удалить' })).toBeVisible();
  await expect(popover.getByRole('link', { name: 'История изменений' })).toBeVisible();

  await page.keyboard.press('Escape');

  await expect(popover).toBeHidden();
  await expect(trigger).toBeFocused();
});

test('opens via keyboard (Enter and Space) on the trigger', async ({ page }) => {
  const name = `E2E меню клавиатура ${Date.now()}`;
  await createWallet(page, { name });

  const trigger = page.locator('.wallet-card', { hasText: name }).getByRole('button', { name: `Действия: ${name}` });
  await trigger.scrollIntoViewIfNeeded();
  await trigger.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('.wallet-card__menu-popover')).toBeVisible();

  await page.keyboard.press('Escape');
  await expect(page.locator('.wallet-card__menu-popover')).toBeHidden();

  await page.keyboard.press('Space');
  await expect(page.locator('.wallet-card__menu-popover')).toBeVisible();
});

test('a click outside closes the menu; only one menu is open at a time', async ({ page }) => {
  const nameA = `E2E меню A ${Date.now()}`;
  const nameB = `E2E меню B ${Date.now()}`;
  await createWallet(page, { name: nameA });
  await createWallet(page, { name: nameB });

  const triggerA = page.locator('.wallet-card', { hasText: nameA }).getByRole('button', { name: `Действия: ${nameA}` });
  const triggerB = page.locator('.wallet-card', { hasText: nameB }).getByRole('button', { name: `Действия: ${nameB}` });

  await clickTrigger(triggerA);
  await expect(page.locator('.wallet-card__menu-popover')).toBeVisible();

  // Opening B's menu must close A's — never two popovers at once.
  await clickTrigger(triggerB);
  await expect(page.locator('.wallet-card__menu-popover')).toHaveCount(1);
  await expect(page.locator('.wallet-card__menu-popover').getByRole('button', { name: 'Изменить' })).toBeVisible();

  await page.locator('h1', { hasText: 'Кошельки' }).click();
  await expect(page.locator('.wallet-card__menu-popover')).toBeHidden();
});

test('a click on the trigger or inside the popover does not dismiss it as an outside click', async ({ page }) => {
  const name = `E2E меню внутр ${Date.now()}`;
  await createWallet(page, { name });

  const card = page.locator('.wallet-card', { hasText: name });
  const trigger = card.getByRole('button', { name: `Действия: ${name}` });
  await clickTrigger(trigger);

  const popover = page.locator('.wallet-card__menu-popover');
  await expect(popover).toBeVisible();

  // Clicking empty space inside the popover panel (not a specific item) must not close it.
  await popover.click({ position: { x: 5, y: 5 } });
  await expect(popover).toBeVisible();
});

test('"Изменить" closes the menu and moves focus into the edit form\'s first field', async ({ page }) => {
  const name = `E2E меню изменить ${Date.now()}`;
  await createWallet(page, { name });

  const card = page.locator('.wallet-card', { hasText: name });
  await clickTrigger(card.getByRole('button', { name: `Действия: ${name}` }));
  await page.locator('.wallet-card__menu-popover').getByRole('button', { name: 'Изменить' }).click();

  await expect(page.locator('.wallet-card__menu-popover')).toBeHidden();
  const nameField = page.locator('input[formcontrolname="name"]');
  await expect(nameField).toBeVisible();
  await expect(nameField).toBeFocused();
});

test('"Удалить" closes the menu, opens the confirm dialog, and focus returns to the trigger once that dialog is cancelled', async ({
  page,
}) => {
  const name = `E2E меню удалить ${Date.now()}`;
  await createWallet(page, { name });

  const card = page.locator('.wallet-card', { hasText: name });
  const trigger = card.getByRole('button', { name: `Действия: ${name}` });
  await clickTrigger(trigger);
  await page.locator('.wallet-card__menu-popover').getByRole('button', { name: 'Удалить' }).click();

  const dialog = page.locator('dialog.app-dialog[open]');
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText(name);

  await page.keyboard.press('Escape');

  await expect(dialog).toBeHidden();
  await expect(trigger).toBeFocused();
});

test('does not offer "Сделать основным" for the wallet that is already primary', async ({ page }) => {
  await page.goto('/wallets');
  const primaryCard = page.locator('.wallet-card', { hasText: 'Основной' }).first();
  const walletName = (await primaryCard.locator('.wallet-card__name').textContent())?.trim() ?? '';
  await clickTrigger(primaryCard.getByRole('button', { name: `Действия: ${walletName}` }));

  const popover = page.locator('.wallet-card__menu-popover');
  await expect(popover).toBeVisible();
  await expect(popover.getByRole('button', { name: 'Сделать основным' })).toHaveCount(0);
});
