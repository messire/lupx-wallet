import { test as base, expect, Page } from '@playwright/test';

/** Dev-only password from backend/README.md / appsettings.Development.json — never used in production. */
export const DEV_PASSWORD = 'ChangeMe123!';

/** Mirrors src/environments/environment.development.ts — the dev server's own API base URL. */
const API_BASE_URL = 'http://localhost:5086/api/v1';

const CREATED_WALLETS_PROP = '__e2eCreatedWalletIds';
const TRACKED_CURRENCIES_PROP = '__e2eTrackedCurrencyCodes';

function trackedWalletIds(page: Page): string[] {
  const store = page as unknown as { [CREATED_WALLETS_PROP]?: string[] };
  return (store[CREATED_WALLETS_PROP] ??= []);
}

function trackedCurrencyCodes(page: Page): string[] {
  const store = page as unknown as { [TRACKED_CURRENCIES_PROP]?: string[] };
  return (store[TRACKED_CURRENCIES_PROP] ??= []);
}

/** Result of {@link apiRequest}: HTTP status and the parsed JSON body (`null` when there is none). */
export interface ApiResult<T = unknown> {
  status: number;
  body: T | null;
}

/**
 * Calls the real API from inside the page with the logged-in user's token — the same way the
 * teardown and `ensureTransferOperationType` do. Requires a logged-in page (token in localStorage).
 */
export async function apiRequest<T = unknown>(
  page: Page,
  method: 'GET' | 'POST' | 'PUT' | 'DELETE',
  path: string,
  body?: unknown,
): Promise<ApiResult<T>> {
  const token = await page.evaluate(() => localStorage.getItem('lupexwallet.auth.token'));
  expect(token, 'apiRequest needs a logged-in page').toBeTruthy();
  return page.evaluate(
    async ({ apiBaseUrl, token, method, path, body }) => {
      const response = await fetch(`${apiBaseUrl}${path}`, {
        method,
        headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
      const parsed = await response.json().catch(() => null);
      return { status: response.status, body: parsed };
    },
    { apiBaseUrl: API_BASE_URL, token, method, path, body },
  ) as Promise<ApiResult<T>>;
}

interface AvailableCurrencyDto {
  code: string;
  name: string;
  isActive: boolean;
}

interface ActiveCurrencyDto {
  id: string;
  code: string;
  name: string;
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
  if (page.isClosed()) {
    return;
  }
  if (ids.length === 0) {
    await cleanupTrackedCurrencies(page);
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
  await cleanupTrackedCurrencies(page);
}

/**
 * Best-effort removal of currencies a scenario registered with `trackCurrencyForCleanup` — after the
 * wallets above are gone (a currency used by a wallet or an operation, even an archived/deleted one,
 * answers 409 and simply stays active). Errors are swallowed for the same reason as above.
 */
async function cleanupTrackedCurrencies(page: Page): Promise<void> {
  const codes = trackedCurrencyCodes(page);
  for (const code of codes) {
    await removeCurrencyIfActive(page, code).catch(() => undefined);
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
 * Makes sure the currency `code` is in the list of available currencies (ADR-0004 §7), which
 * is filled only by a rates refresh — a fresh database has none. If it is missing, runs
 * `POST /exchange-rates/refresh` against the real Frankfurter service (network required) and
 * checks again. Returns `true` when a refresh was needed, i.e. a page that was opened earlier
 * holds a stale (empty) list and must be reloaded. Fails with a clear message if the code is
 * still absent — without network access these e2e scenarios cannot run.
 */
export async function ensureAvailableCurrency(page: Page, code: string): Promise<boolean> {
  const isAvailable = async () => {
    const { body } = await apiRequest<AvailableCurrencyDto[]>(page, 'GET', '/currencies/available');
    return (body ?? []).some((currency) => currency.code === code);
  };

  if (await isAvailable()) {
    return false;
  }
  // 200 or 502 — only the resulting list matters; a 502 means the source is unreachable and fails below.
  await apiRequest(page, 'POST', '/exchange-rates/refresh');
  if (!(await isAvailable())) {
    throw new Error(
      `Currency ${code} is not in GET /currencies/available even after POST /exchange-rates/refresh — ` +
        'the e2e suite needs network access to the real Frankfurter service (https://api.frankfurter.dev/v2/).',
    );
  }
  return true;
}

/** {@link ensureAvailableCurrency} for the page the test is on: reloads it if the list had to be filled meanwhile. */
export async function ensureAvailableCurrencyOnPage(page: Page, code: string): Promise<void> {
  if (await ensureAvailableCurrency(page, code)) {
    await page.reload();
  }
}

/**
 * Makes sure the currency is active (`POST /currencies {code}`; 201 created, 409 already active) —
 * for scenarios that need an active currency without creating a wallet. Idempotent, not torn down
 * (see `trackCurrencyForCleanup` for the scenarios that want it removed again).
 */
export async function ensureCurrency(page: Page, code: string): Promise<void> {
  await ensureAvailableCurrency(page, code);
  const { status } = await apiRequest(page, 'POST', '/currencies', { code });
  expect([201, 409], `POST /currencies {code: ${code}} answered ${status}`).toContain(status);
}

/**
 * Removes the currency from the active ones if it is active (`DELETE /currencies/{id}`).
 * Returns the response status, or `null` when it was not active. 409 — still used by a wallet or an operation.
 */
export async function removeCurrencyIfActive(page: Page, code: string): Promise<number | null> {
  const { body } = await apiRequest<{ data: ActiveCurrencyDto[] }>(page, 'GET', '/currencies?limit=100');
  const active = body?.data.find((currency) => currency.code === code);
  if (!active) {
    return null;
  }
  return (await apiRequest(page, 'DELETE', `/currencies/${active.id}`)).status;
}

/** Registers a currency to be removed from the active ones when the test finishes (after its wallets are cleaned up). */
export function trackCurrencyForCleanup(page: Page, code: string): void {
  trackedCurrencyCodes(page).push(code);
}

/**
 * Picks `code` in the wallet form's currency combobox (ADR-0004 §7: the choice comes from the list of
 * available currencies, there is no quick-add): type the code, choose the matching "CODE — name" option.
 */
export async function selectWalletCurrency(page: Page, code: string): Promise<void> {
  const combobox = page.locator('app-currency-select').getByRole('combobox');
  await expect(combobox).toBeEnabled();
  await combobox.click();
  await combobox.fill(code);
  await page.getByRole('option', { name: new RegExp(`^${code} —`) }).click();
  await expect(combobox).toHaveValue(new RegExp(`^${code} —`));
}

/**
 * Creates a wallet type via the Wallets create-form quick-add row if none exists yet, picks the currency
 * from the available ones (default EUR — every caller assumes it: transfer/balance-history/exchange-rates
 * scenarios need same-currency wallets), then fills and submits the form. Returns once the new wallet
 * card is visible, and returns the created wallet's id (also tracked automatically for teardown by the
 * `page` fixture above — no extra call needed at test sites). `name` should be unique per test to avoid
 * clashing with wallets left over from other scenarios sharing the same DB.
 *
 * The available list is filled only by a rates refresh, which `ensureAvailableCurrencyOnPage` triggers
 * against the real Frankfurter service when the code is missing (network required). The server activates
 * the currency itself when the wallet is created.
 */
export async function createWallet(
  page: Page,
  options: { name: string; initialBalance?: string; accountingStartDate?: string; currencyCode?: string },
): Promise<string> {
  const currencyCode = options.currencyCode ?? 'EUR';
  await ensureAvailableCurrencyOnPage(page, currencyCode);

  // `.first()`: on an empty database the page shows the empty state, which has its own
  // "Добавить кошелек" button next to the heading one (strict-mode violation otherwise).
  await page.getByRole('button', { name: 'Добавить кошелек' }).first().click();

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

  await selectWalletCurrency(page, currencyCode);

  await page.locator('input[formcontrolname="name"]').fill(options.name);
  if (options.initialBalance) {
    await page.locator('input[formcontrolname="initialBalanceAmount"]').fill(options.initialBalance);
  }

  if (options.accountingStartDate) {
    // Defaults to today; operations/transfers dated before it are rejected, so scenarios that
    // create a past-dated record (ADR-0002: deletable with any date) need an earlier start date.
    await page.locator('input[formcontrolname="accountingStartDate"]').fill(options.accountingStartDate);
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
 * UTC calendar date `daysAgo` days before today, as `YYYY-MM-DD` (the format of `<input type="date">`).
 * UTC on purpose: the app's forms compute "today" as `new Date().toISOString().slice(0, 10)`.
 */
export function isoDateDaysAgo(daysAgo: number): string {
  const date = new Date();
  date.setUTCDate(date.getUTCDate() - daysAgo);
  return date.toISOString().slice(0, 10);
}

/**
 * Creates an operation type via the Reference Data screen if it doesn't already
 * exist — the Operations form has no quick-add for it (unlike the wallet type
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

/**
 * Makes sure an active operation type with behavior "Transfer" exists — creating a transfer
 * is impossible without one, and a fresh database has none (the Reference Data screen
 * deliberately cannot create it: Transfer is a system-only behavior kind). Created through
 * the API instead, like `cleanupCreatedWallets` does for teardown; idempotent, not torn down.
 * Requires a logged-in page (reads the auth token from localStorage).
 */
export async function ensureTransferOperationType(page: Page): Promise<void> {
  const token = await page.evaluate(() => localStorage.getItem('lupexwallet.auth.token'));
  expect(token, 'ensureTransferOperationType needs a logged-in page').toBeTruthy();

  const result = await page.evaluate(
    async ({ apiBaseUrl, token }) => {
      const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
      const kinds = (await (await fetch(`${apiBaseUrl}/operation-behavior-kinds`, { headers })).json()) as Array<{
        id: string;
        code: string;
      }>;
      const transferKind = kinds.find((kind) => kind.code === 'Transfer');
      if (!transferKind) {
        return 'no-transfer-behavior-kind';
      }
      const types = (await (await fetch(`${apiBaseUrl}/operation-types?limit=100`, { headers })).json()) as {
        data: Array<{ behaviorKindId: string; isActive: boolean }>;
      };
      if (types.data.some((type) => type.behaviorKindId === transferKind.id && type.isActive)) {
        return 'exists';
      }
      const created = await fetch(`${apiBaseUrl}/operation-types`, {
        method: 'POST',
        headers,
        body: JSON.stringify({ name: 'E2E перевод', behaviorKindId: transferKind.id }),
      });
      return created.ok ? 'created' : `create-failed-${created.status}`;
    },
    { apiBaseUrl: API_BASE_URL, token },
  );

  expect(['exists', 'created']).toContain(result);
}
