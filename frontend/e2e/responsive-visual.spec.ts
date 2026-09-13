import { test, expect } from '@playwright/test';
import { login, createWallet } from './fixtures';
import path from 'node:path';

/**
 * Visual verification, not a pass/fail assertion suite: captures the Wallets
 * screen at the three required breakpoints plus 200% zoom, with a long
 * Russian label and a large amount, so a human can review layout for
 * overflow/clipping. Screenshots are saved under e2e/screenshots/manual-review/
 * (see docs/PROGRESS.md for how these were reviewed).
 */
test('Wallets screen at 360/768/1440px and 200% zoom, with a long name and a large amount', async ({ page }) => {
  await login(page);

  const longName = `Очень длинное название кошелька для проверки переноса и обрезки текста на разных экранах ${Date.now()}`;
  await createWallet(page, { name: longName, initialBalance: '9876543.21' });

  const outDir = path.join(__dirname, 'screenshots', 'manual-review');

  for (const width of [360, 768, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    await page.waitForTimeout(150); // let the responsive layout settle (container queries, grid reflow)
    await page.screenshot({ path: `${outDir}/wallets-${width}px.png`, fullPage: true });
  }

  // 200% page zoom at desktop width (CSS zoom — the closest equivalent to
  // browser page-zoom controllable without a CDP session).
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.evaluate(() => {
    document.documentElement.style.zoom = '2';
  });
  await page.waitForTimeout(150);
  await page.screenshot({ path: `${outDir}/wallets-1440px-200pct-zoom.png`, fullPage: true });
  await page.evaluate(() => {
    document.documentElement.style.zoom = '1';
  });

  // The long name and the large amount must still be fully present in the DOM
  // text (adaptive shrinking/wrapping, never truncation of significant digits —
  // ui-kit.md §3) regardless of viewport.
  await expect(page.locator('.wallet-card', { hasText: longName })).toContainText('9 876 543.21');
});
