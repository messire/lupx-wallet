import { test, login, createWallet } from './fixtures';
import path from 'node:path';

/** Ad-hoc capture — NOT a regression spec (no assertions) — for the three confirmed-bug fixes. */
test('capture the three fixes: select arrow, wallet card menu, mobile hamburger icon', async ({ page }) => {
  await login(page);
  const outDir = path.join(__dirname, 'screenshots', 'manual-review');

  await page.setViewportSize({ width: 1440, height: 900 });

  // Fix 1: select arrow visible (filters/forms).
  await page.getByRole('button', { name: 'Добавить кошелек' }).click();
  await page.waitForTimeout(150);
  await page.screenshot({ path: `${outDir}/fix1-select-arrow-1440px.png`, clip: { x: 0, y: 0, width: 720, height: 500 } });
  await page.getByRole('button', { name: 'Отмена', exact: true }).click();

  // Fix 2: wallet card menu, open.
  const name = `Отчёт кошелёк ${Date.now()}`;
  await createWallet(page, { name });
  const trigger = page.locator('.wallet-card', { hasText: name }).getByRole('button', { name: `Действия: ${name}` });
  await trigger.scrollIntoViewIfNeeded();
  await page.waitForTimeout(150);
  await trigger.click();
  await page.waitForTimeout(150);
  await page.screenshot({ path: `${outDir}/fix2-wallet-menu-open-1440px.png` });
  await page.keyboard.press('Escape');

  // Fix 3: mobile hamburger icon (stacked bars).
  await page.setViewportSize({ width: 360, height: 800 });
  await page.goto('/wallets');
  await page.waitForTimeout(150);
  await page.screenshot({ path: `${outDir}/fix3-mobile-menu-icon-360px.png`, clip: { x: 0, y: 0, width: 360, height: 90 } });
});
