import { test as base, expect, Page } from '@playwright/test';

/** Dev-only password from backend/README.md / appsettings.Development.json — never used in production. */
export const DEV_PASSWORD = 'ChangeMe123!';

/** Mirrors src/environments/environment.development.ts — the dev server's own API base URL. */
const API_BASE_URL = 'http://localhost:5086/api/v1';

const CREATED_WALLETS_PROP = '__e2eCreatedWalletIds';

function trackedWalletIds(page: Page): string[] {
  const store = page as unknown as { [CREATED_WALLETS_PROP]?: string[] };
  return (store[CREATED_WALLETS_PROP] ??= []);
}

/**
 * Best-effort teardown for every wallet `createWallet` created during a test
 * (regardless of pass/fail) — without this, each E2E run leaves its throwaway
 * wallets in the shared dev database forever, and they accumulate across
 * sessions (found and manually purged once — see docs/PROGRESS.md 2026-09-16).
 * `DELETE /wallets/{id}` (UC-04) removes a wallet outright but 409s if it has
 * operation/snapshot history or is primary; those fall back to `/archive`
 * (UC-03), which at least drops them out of default wallet lists/dropdowns.
 * Errors are swallowed — a flaky cleanup call must never fail the test itself.
 */
async function cleanupCreatedWallets(page: Page): Promise<void> {
  const ids = trackedWalletIds(page);
  if (ids.length === 0 || page.isClosed()) {
    return;
  }
  const token = await page.evaluate(() => localStorage.getItem('lupexwallet.auth.token')).catch(() => null);
  if (!token) {
    return;
  }
  for (const id of ids) {
    await page
      .evaluate(
        async ({ apiBaseUrl, id, token }) => {
          const headers = { Authorization: `Bearer ${token}` };
          const deleted = await fetch(`${apiBaseUrl}/wallets/${id}`, { method: 'DELETE', headers });
          if (deleted.status === 409) {
            await fetch(`${apiBaseUrl}/wallets/${id}/archive`, { method: 'POST', headers });
          }
        },
        { apiBaseUrl: API_BASE_URL, id, token },
      )
      .catch(() => {
        // Best-effort: a page already navigating away, or a wallet another
        // test already cleaned up, must not fail this test's own result.
      });
  }
}

/**
 * `test`/`expect` for every E2E spec in this suite — import both from here
 * instead of '@playwright/test' directly. Wraps the built-in `page` fixture
 * so every wallet created via `createWallet` below is automatically deleted
 * (or archived, if it gained history) once the test finishes.
 */
export const test = base.extend({
  page: async ({ page }, use) => {
    await use(page);
    await cleanupCreatedWallets(page);
  },
});

export { expect };

export async function login(page: Page): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('Пароль').fill(DEV_PASSWORD);
  await page.getByRole('button', { name: 'Войти' }).click();
  await expect(page).toHaveURL(/\/wallets$/);
}

/**
 * Creates a wallet type and currency via the Wallets create-form quick-add
 * rows if none exist yet, then fills and submits the form. Returns once the
 * new wallet card is visible, and returns the created wallet's id (also
 * tracked automatically for teardown by the `page` fixture above — no extra
 * call needed at test sites). `name` should be unique per test to avoid
 * clashing with wallets left over from other scenarios sharing the same DB.
 */
export async function createWallet(page: Page, options: { name: string; initialBalance?: string }): Promise<string> {
  await page.getByRole('button', { name: 'Добавить кошелек' }).click();

  const typeSelect = page.locator('select[formcontrolname="walletTypeId"]');
  // `[disabled]="walletTypesLoading()"` clears once the list has actually loaded —
  // without waiting for it, the option count below is a race against that request
  // (count() has no auto-retry, unlike `expect(locator)...`) and can read the
  // placeholder-only state, quick-adding a type that already exists and getting a
  // 500 (unique constraint) instead of just selecting it.
  await expect(typeSelect).toBeEnabled();
  if ((await typeSelect.locator('option').count()) <= 1) {
    await page.getByRole('button', { name: '+ добавить тип' }).click();
    await page.getByPlaceholder('Название типа').fill('E2E тип кошелька');
    const typeQuickAdd = page.locator('.quick-add').filter({ has: page.getByPlaceholder('Название типа') });
    await typeQuickAdd.getByRole('button', { name: 'Добавить' }).click();
    await expect(typeSelect.locator('option')).not.toHaveCount(1);
  }
  await typeSelect.selectOption({ index: 1 });

  // Selected by label, not position: the dev database now holds more than one
  // currency (found during the 2026-09-16 cleanup — see docs/PROGRESS.md), so
  // `{ index: 1 }` is no longer reliably EUR. Every caller of createWallet
  // assumes EUR (transfer/balance-history/exchange-rates scenarios need same-
  // currency wallets), so this always ensures EUR specifically, not just "some
  // currency exists".
  const currencySelect = page.locator('select[formcontrolname="currencyId"]');
  await expect(currencySelect).toBeEnabled();
  if (!(await currencySelect.locator('option', { hasText: 'EUR' }).count())) {
    await page.getByRole('button', { name: '+ добавить валюту' }).click();
    const currencyCombobox = page.getByRole('combobox').last();
    await currencyCombobox.click();
    await currencyCombobox.fill('EUR');
    await page.getByRole('option', { name: /EUR/ }).first().click();
    await page.locator('.quick-add').getByRole('button', { name: 'Добавить' }).click();
    await expect(currencySelect.locator('option', { hasText: 'EUR' })).toHaveCount(1);
  }
  // The option's visible text is "{code} — {name}" (e.g. "EUR — Euro"), not
  // just the code, so `selectOption({ label })` needs an exact match we don't
  // have — select by the option's own `value` (the currency id) instead.
  const eurValue = await currencySelect.locator('option', { hasText: 'EUR' }).first().getAttribute('value');
  await currencySelect.selectOption(eurValue ?? { index: 1 });

  await page.locator('input[formcontrolname="name"]').fill(options.name);
  if (options.initialBalance) {
    await page.locator('input[formcontrolname="initialBalanceAmount"]').fill(options.initialBalance);
  }

  const [response] = await Promise.all([
    page.waitForResponse(
      (res) => res.request().method() === 'POST' && new URL(res.url()).pathname === '/api/v1/wallets',
    ),
    page.getByRole('button', { name: 'Создать', exact: true }).click(),
  ]);
  await expect(page.locator('.wallet-card', { hasText: options.name })).toBeVisible();

  const created = (await response.json().catch(() => null)) as { id?: string } | null;
  if (created?.id) {
    trackedWalletIds(page).push(created.id);
  }
  return created?.id ?? '';
}

/**
 * Creates an operation type via the Reference Data screen if it doesn't already
 * exist — the Operations form has no quick-add for it (unlike wallet type/currency
 * in the wallet form), so tests that need one must seed it here first. Reused
 * across tests by design (idempotent, checks for an existing row first) — not
 * torn down, since it doesn't accumulate the way per-test wallets do.
 */
export async function ensureOperationType(
  page: Page,
  name: string,
  behaviorKindLabel: 'Доход' | 'Расход' | 'Ручная корректировка',
): Promise<void> {
  await page.goto('/reference-data');
  await page.getByRole('tab', { name: 'Типы операций' }).click();

  const existingRow = page.locator('.ref-table tr', { hasText: name });
  if (await existingRow.count()) {
    return;
  }

  // Reference Data keeps all three tab panels in the DOM ([hidden] toggles the
  // active one) — scope to the visible panel to avoid matching the wallet-types
  // form's same-named `name` field.
  const panel = page.locator('.tab-panel:not([hidden])');
  await panel.locator('input[formcontrolname="name"]').fill(name);
  await panel.locator('select[formcontrolname="behaviorKindId"]').selectOption({ label: behaviorKindLabel });
  await panel.getByRole('button', { name: 'Добавить', exact: true }).click();
  await expect(page.locator('.ref-table', { hasText: name })).toBeVisible();
}
