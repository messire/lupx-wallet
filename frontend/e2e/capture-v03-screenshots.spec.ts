import { test, login } from './fixtures';
import path from 'node:path';

/**
 * Ad-hoc capture script for the UI kit v0.3 rollout review — NOT a permanent
 * regression spec (no assertions). Screenshots existing screens at the three
 * required breakpoints without creating new test data, to avoid the Wallets
 * list pagination limitation (loadWallets() has no `limit`/paging, and the
 * shared dev DB already has many wallets from repeated E2E runs, so newly
 * created wallets are not guaranteed to appear on the first page).
 */
test('capture Wallets/Operations at 360/768/1440px', async ({ page }) => {
  await login(page);
  const outDir = path.join(__dirname, 'screenshots', 'manual-review');

  for (const width of [360, 768, 1440]) {
    await page.goto('/wallets');
    await page.setViewportSize({ width, height: 900 });
    await page.waitForTimeout(200);
    await page.screenshot({ path: `${outDir}/v03-wallets-${width}px.png`, fullPage: true });
  }

  await page.goto('/wallets');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.getByRole('button', { name: 'Добавить кошелек' }).click();
  await page.waitForTimeout(200);
  await page.screenshot({ path: `${outDir}/v03-wallet-form-1440px.png`, fullPage: true });

  for (const width of [360, 768, 1440]) {
    await page.goto('/operations');
    await page.setViewportSize({ width, height: 900 });
    await page.waitForTimeout(200);
    await page.screenshot({ path: `${outDir}/v03-operations-${width}px.png`, fullPage: true });
  }

  await page.goto('/operations');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.locator('.page-heading').getByRole('button', { name: 'Добавить операцию' }).click();
  await page.waitForTimeout(200);
  await page.screenshot({ path: `${outDir}/v03-operation-form-dialog-1440px.png`, fullPage: true });
});
