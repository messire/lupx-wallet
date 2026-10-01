import { Page } from '@playwright/test';
import { test, expect, login, apiRequest } from './fixtures';

// The real Frankfurter source is not reachable from CI/sandbox networks (known
// limitation, docs/PROGRESS.md "Известные ограничения") — the manual-refresh
// response itself is mocked deterministically here rather than depending on
// it being reachable. Everything else (login, the page's GET requests) goes
// through the real backend, same as the rest of this suite.

test.beforeEach(async ({ page }) => {
  await login(page);
});

/** Captures the body of the page's initial `GET /exchange-rates/latest` response. */
function captureLatestResponse(page: Page): { get(): unknown } {
  let body: unknown;
  page.on('response', (response) => {
    if (response.request().method() === 'GET' && response.url().includes('/api/v1/exchange-rates/latest')) {
      void response.json().then((json) => (body = json));
    }
  });
  return { get: () => body };
}

test('"Обновить сейчас" blocks the button during the request, keeps the table visible, and updates the time on success', async ({
  page,
}) => {
  await page.goto('/exchange-rates');

  // Scoped to the page heading: when no rates have ever been fetched, the
  // empty state below renders a second "Обновить сейчас" button with the
  // same accessible name — an unscoped locator would be ambiguous.
  const refreshButton = page.locator('.page-heading').getByRole('button', { name: 'Обновить сейчас' });
  await expect(refreshButton).toBeEnabled();

  const statusValue = page.locator('.rates-status__row p').last();
  const timeBefore = await statusValue.textContent();

  await page.route('**/api/v1/exchange-rates/refresh', async (route) => {
    // Deterministic delay so the in-flight (blocked) state can be observed
    // instead of racing a response that may resolve before assertions run.
    await new Promise((resolve) => setTimeout(resolve, 500));
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        lastSuccessfulUpdate: new Date().toISOString(),
        rates: [{ fromCurrencyId: 'cur-usd', toCurrencyId: 'cur-eur', rate: '0.9123456789' }],
      }),
    });
  });

  await refreshButton.click();
  // While loading, app-button swaps its label for "Обновление…", so `refreshButton` (matched by the
  // "Обновить сейчас" name) does not resolve until the request is over — assert on the button itself.
  await expect(page.locator('.page-heading button')).toBeDisabled();
  // No skeleton reappears and previous content is not cleared while the manual
  // refresh is in flight (C5) — only the primary-load skeleton is allowed, and
  // only before this navigation's first response, already resolved by now.
  await expect(page.locator('app-skeleton')).toHaveCount(0);

  await expect(refreshButton).toBeEnabled({ timeout: 15000 });
  await expect(page.getByText('Курсы валют обновлены.')).toBeVisible();

  const timeAfter = await statusValue.textContent();
  expect(timeAfter).not.toBe(timeBefore);
});

test('a 502 from the refresh endpoint keeps the previous rows and time, with a visible "source unavailable" notice', async ({
  page,
}) => {
  const latest = captureLatestResponse(page);
  await page.goto('/exchange-rates');
  await expect.poll(() => latest.get()).toBeTruthy();

  // Scoped to the page heading — see the other test for why an unscoped
  // locator is ambiguous when the empty state's own button is also present.
  const refreshButton = page.locator('.page-heading').getByRole('button', { name: 'Обновить сейчас' });
  await expect(refreshButton).toBeEnabled();

  const rowsBefore = await page.locator('.rates-table tbody tr').count();
  const statusValue = page.locator('.rates-status__row p').last();
  const timeBefore = await statusValue.textContent();

  await page.route('**/api/v1/exchange-rates/refresh', (route) =>
    // Per openapi.yaml, a 502 from this endpoint carries the same
    // LatestExchangeRates body as the last successful GET — mirrored here
    // exactly, so the assertions below only verify the page's own handling
    // of that body, not a fabricated fixture.
    route.fulfill({ status: 502, contentType: 'application/json', body: JSON.stringify(latest.get()) }),
  );

  await refreshButton.click();
  await expect(refreshButton).toBeEnabled({ timeout: 15000 });

  if (rowsBefore > 0) {
    await expect(page.getByText('Источник недоступен')).toBeVisible();
    await expect(page.locator('.rates-table tbody tr')).toHaveCount(rowsBefore);
    await expect(statusValue).toHaveText(timeBefore ?? '');
  } else {
    // No previous data at all — a dedicated empty state, not a fabricated row.
    await expect(page.getByText(/недоступен/)).toBeVisible();
    await expect(page.locator('.rates-table')).toHaveCount(0);
  }
});

test('without a primary wallet "Обновить сейчас" still loads the list of available currencies', async ({ page }) => {
  // The page decides "no primary wallet" from GET /wallets. In a database that already has a primary wallet
  // only that one read is replaced by an empty list — the refresh itself is real (Frankfurter, network
  // required). In a database without wallets nothing is replaced and the backend path is exercised for real.
  const wallets = await apiRequest<{ data: Array<{ isPrimary: boolean }> }>(page, 'GET', '/wallets?includeArchived=true&limit=100');
  const hasPrimary = (wallets.body?.data ?? []).some((wallet) => wallet.isPrimary);
  if (hasPrimary) {
    await page.route(
      (url) => url.pathname === '/api/v1/wallets',
      (route) =>
        route.request().method() === 'GET'
          ? route.fulfill({
              status: 200,
              contentType: 'application/json',
              body: JSON.stringify({ data: [], pagination: { nextCursor: null, hasMore: false } }),
            })
          : route.continue(),
    );
  }

  await page.goto('/exchange-rates');
  await expect(page.getByText('Основной кошелёк не назначен')).toBeVisible();
  await expect(page.getByText('Кнопка «Обновить сейчас» и без него загружает список доступных валют.')).toBeVisible();

  const refreshButton = page.locator('.page-heading').getByRole('button', { name: 'Обновить сейчас' });
  await expect(refreshButton).toBeEnabled();
  await refreshButton.click();

  await expect(page.getByText('Список доступных валют обновлён.')).toBeVisible({ timeout: 30000 });
  await expect(page.getByText('Курсы валют обновлены.')).toHaveCount(0);
  // Still the "no primary wallet" state: no rate rows are shown.
  await expect(page.locator('.rates-table')).toHaveCount(0);

  const available = await apiRequest<Array<{ code: string }>>(page, 'GET', '/currencies/available');
  expect(available.status).toBe(200);
  expect(available.body?.length ?? 0).toBeGreaterThan(0);
  expect(available.body?.map((currency) => currency.code)).toContain('EUR');
});
