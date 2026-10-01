import type { Page } from '@playwright/test';
import {
  apiRequest,
  createOperation,
  createWallet,
  ensureOperationType,
  expect,
  filterOperationsByWallet,
  isoDateDaysAgo,
  login,
  setStatedBalance,
  test,
} from './fixtures';

// Login is limited to 5 requests/minute and every test logs in (beforeEach): this file stays at 3 scenarios.
test.beforeEach(async ({ page }) => {
  await login(page);
});

interface OperationDto {
  id: string;
  walletId: string;
  operationDate: string;
  amount: { amount: string };
  isCorrection: boolean;
}

interface BalanceDto {
  balance: { amount: string };
}

async function balanceOn(page: Page, walletId: string, date: string): Promise<number> {
  const { status, body } = await apiRequest<BalanceDto>(page, 'GET', `/wallets/${walletId}/balance?date=${date}`);
  expect(status, `GET balance on ${date}`).toBe(200);
  return Number(body?.balance.amount);
}

async function correctionsOf(page: Page, walletId: string): Promise<OperationDto[]> {
  const { body } = await apiRequest<{ data: OperationDto[] }>(page, 'GET', `/operations?walletId=${walletId}&limit=100`);
  return (body?.data ?? []).filter((operation) => operation.isCorrection);
}

/**
 * ADR-0003 §6, the worked example: a stated balance is reconciled with the operations by a system correction
 * amount, which is recalculated when an earlier operation appears and replaced by a repeated input.
 */
test('stating a balance adds a system correction row that follows later operations and is replaced by a repeated input', async ({
  page,
}) => {
  test.setTimeout(180_000);
  const walletName = `E2E заявленный баланс ${Date.now()}`;
  const walletId = await createWallet(page, { name: walletName, accountingStartDate: isoDateDaysAgo(30) });
  await ensureOperationType(page, 'E2E доход', 'Доход');
  await ensureOperationType(page, 'E2E расход', 'Расход');

  const day29 = isoDateDaysAgo(29);
  const day20 = isoDateDaysAgo(20);
  const day15 = isoDateDaysAgo(15);
  const day10 = isoDateDaysAgo(10);

  await page.goto('/operations');
  await filterOperationsByWallet(page, walletName);
  const rows = page.locator('tbody tr');

  // +500 and −200: the operations alone give 300 at the end of day 20.
  await createOperation(page, { walletName, typeName: 'E2E доход', amount: '500', date: day29 });
  await createOperation(page, { walletName, typeName: 'E2E расход', amount: '200', date: day20 });
  await expect(rows).toHaveCount(2);

  // Stating 350 on day 20 -> "Неизвестные доходы +50" with the "Система" badge, in that day, after its operation.
  expect(await setStatedBalance(page, { walletName, date: day20, amount: '350' })).toBe(201);
  await expect(page.getByText(`Баланс на ${day20} указан`)).toBeVisible();
  await expect(rows).toHaveCount(3);

  const incomeCorrection = rows.filter({ hasText: 'Неизвестные доходы' });
  await expect(incomeCorrection).toHaveCount(1);
  await expect(incomeCorrection).toHaveClass(/is-system/);
  await expect(incomeCorrection.locator('td.date-col')).toContainText(day20);
  await expect(incomeCorrection.locator('td.date-col app-badge', { hasText: 'Система' })).toBeVisible();
  await expect(incomeCorrection.locator('td.amount-col')).toContainText('+50');
  await expect(incomeCorrection).toContainText('Баланс на конец дня: 350');
  // Inside the day the correction amount comes after the regular operation: rows are date-ordered, 29 / 20 / 20.
  await expect(rows.nth(0).locator('td.date-col')).toContainText(day29);
  await expect(rows.nth(1)).not.toHaveClass(/is-system/);
  await expect(rows.nth(1).locator('td.date-col')).toContainText(day20);
  await expect(rows.nth(2)).toHaveClass(/is-system/);
  await expect(rows.nth(2).locator('td.date-col')).toContainText(day20);
  // The italic style is a secondary cue (the badge text is the primary one).
  await expect(incomeCorrection.locator('td').first()).toHaveCSS('font-style', 'italic');

  // A system row has no "···" menu, no edit/delete/restore: only the change history link, named by title and date.
  await expect(incomeCorrection.getByRole('button')).toHaveCount(0);
  await expect(incomeCorrection.locator('td.actions')).not.toContainText('···');
  const history = incomeCorrection.locator('td.actions').getByRole('link', { name: `История изменений: Неизвестные доходы, ${day20}` });
  await expect(history).toBeVisible();
  await expect(incomeCorrection.locator('td.actions a')).toHaveCount(1);

  // +400 on day 10, then stating 600 on day 10 -> "Неизвестные траты −150" (500 − 200 + 50 + 400 = 750 → 600).
  await createOperation(page, { walletName, typeName: 'E2E доход', amount: '400', date: day10 });
  expect(await setStatedBalance(page, { walletName, date: day10, amount: '600' })).toBe(201);
  await expect(page.getByText(`Баланс на ${day10} указан`)).toBeVisible();

  const expenseCorrection = rows.filter({ hasText: 'Неизвестные траты' });
  await expect(expenseCorrection).toHaveCount(1);
  await expect(expenseCorrection.locator('td.date-col')).toContainText(day10);
  await expect(expenseCorrection.locator('td.amount-col')).toContainText('−150');
  await expect(expenseCorrection.locator('td.date-col app-badge', { hasText: 'Система' })).toBeVisible();

  // An expense of 100 on day 15 (between the two stated dates): day 20 is unaffected, day 10 becomes −50.
  await createOperation(page, { walletName, typeName: 'E2E расход', amount: '100', date: day15 });
  await expect(expenseCorrection).toHaveCount(1);
  await expect(expenseCorrection.locator('td.amount-col')).toContainText('−50');
  await expect(expenseCorrection.locator('td.amount-col')).not.toContainText('−150');
  await expect(incomeCorrection).toHaveCount(1);
  await expect(incomeCorrection.locator('td.amount-col')).toContainText('+50');

  // The wallet card shows the stated 600.
  await page.goto('/wallets');
  await expect(page.locator('.wallet-card', { hasText: walletName }).locator('.wallet-card__balance')).toContainText('600');

  // The balances at the stated dates, through the API: 350 and 600.
  expect(await balanceOn(page, walletId, day20)).toBe(350);
  expect(await balanceOn(page, walletId, day10)).toBe(600);

  // Stating 350 on day 20 again replaces the value: "заменён" toast, the same row (same id), no second row.
  const before = (await correctionsOf(page, walletId)).find((operation) => operation.operationDate === day20);
  expect(before, 'correction on day 20 before the repeated input').toBeTruthy();

  await page.goto('/operations');
  await filterOperationsByWallet(page, walletName);
  await expect(rows).toHaveCount(6);
  expect(await setStatedBalance(page, { walletName, date: day20, amount: '350' })).toBe(200);
  await expect(page.getByText(`Баланс на ${day20} заменён`)).toBeVisible();
  await expect(rows).toHaveCount(6);
  await expect(rows.filter({ hasText: 'Неизвестные доходы' })).toHaveCount(1);

  const after = await correctionsOf(page, walletId);
  expect(after.filter((operation) => operation.operationDate === day20).map((operation) => operation.id)).toEqual([before!.id]);
  expect(await balanceOn(page, walletId, day20)).toBe(350);
  expect(await balanceOn(page, walletId, day10)).toBe(600);

  // A different value on the same date replaces it as well (still one row for the date).
  expect(await setStatedBalance(page, { walletName, date: day20, amount: '320' })).toBe(200);
  expect(await balanceOn(page, walletId, day20)).toBe(320);
  expect((await correctionsOf(page, walletId)).filter((operation) => operation.operationDate === day20)).toHaveLength(1);
});

test('the "Указать баланс" dialog limits the date, rejects a malformed amount and returns focus on Escape', async ({ page }) => {
  const walletName = `E2E диапазон баланса ${Date.now()}`;
  const startDate = isoDateDaysAgo(30);
  await createWallet(page, { name: walletName, accountingStartDate: startDate });

  await page.goto('/operations');
  await filterOperationsByWallet(page, walletName);
  const trigger = page.locator('.page-heading').getByRole('button', { name: 'Указать баланс' });
  await trigger.click();

  const dialog = page.getByRole('dialog', { name: 'Указать баланс на дату' });
  await expect(dialog).toBeVisible();
  await dialog.locator('select[formcontrolname="walletId"]').selectOption({ label: walletName });

  // The date input is limited to [accounting start .. today] and defaults to today.
  const date = dialog.locator('input[formcontrolname="date"]');
  await expect(date).toHaveAttribute('min', startDate);
  await expect(date).toHaveAttribute('max', isoDateDaysAgo(0));
  await expect(date).toHaveValue(isoDateDaysAgo(0));

  // The hint is linked to the amount through aria-describedby.
  const amount = dialog.locator('input[formcontrolname="amount"]');
  const hint = dialog.locator('.app-field__hint');
  await expect(hint).toContainText('Введите фактический баланс на конец дня');
  const hintId = await hint.getAttribute('id');
  await expect(amount).toHaveAttribute('aria-describedby', hintId!);

  // A date before the accounting start is rejected on the client; nothing is sent.
  let putRequests = 0;
  await page.route('**/stated-balances/**', (route) => {
    putRequests += 1;
    return route.continue();
  });
  await date.fill(isoDateDaysAgo(31));
  await amount.fill('100');
  await dialog.getByRole('button', { name: 'Указать баланс', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('раньше начала учёта кошелька');
  expect(putRequests).toBe(0);

  // A comma as the decimal separator is rejected with the expected format spelled out.
  await date.fill(isoDateDaysAgo(5));
  await amount.fill('350,50');
  await dialog.getByRole('button', { name: 'Указать баланс', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('число с точкой');
  expect(putRequests).toBe(0);

  // Escape closes the dialog without stating anything and returns focus to the button that opened it.
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(trigger).toBeFocused();
  expect(putRequests).toBe(0);
  await expect(page.locator('tbody tr')).toHaveCount(0);
});

test('an adjustment operation offers only the signed change (Delta) and keeps working next to a stated balance', async ({ page }) => {
  const walletName = `E2E корректировка дельтой ${Date.now()}`;
  const walletId = await createWallet(page, { name: walletName, initialBalance: '100', accountingStartDate: isoDateDaysAgo(30) });
  await ensureOperationType(page, 'E2E корректировка', 'Ручная корректировка');

  await page.goto('/operations');
  await filterOperationsByWallet(page, walletName);
  await page.locator('.page-heading').getByRole('button', { name: 'Добавить операцию' }).click();

  const dialog = page.getByRole('dialog', { name: 'Новая операция' });
  await dialog.locator('select[formcontrolname="walletId"]').selectOption({ label: walletName });
  await dialog.locator('select[formcontrolname="operationTypeId"]').selectOption({ label: 'E2E корректировка' });

  // No choice between a delta and a new absolute value any more: only "Изменение на сумму (+/−)".
  await expect(dialog.getByText('Изменение на сумму (+/−)')).toBeVisible();
  await expect(dialog.getByText('Новое значение баланса')).toHaveCount(0);
  await expect(dialog.locator('input[type="radio"]')).toHaveCount(0);

  await dialog.locator('input[formcontrolname="amount"]').fill('-30');
  await dialog.locator('input[formcontrolname="operationDate"]').fill(isoDateDaysAgo(12));
  const [request] = await Promise.all([
    page.waitForRequest((req) => req.method() === 'POST' && new URL(req.url()).pathname === '/api/v1/operations'),
    dialog.getByRole('button', { name: 'Создать', exact: true }).click(),
  ]);
  expect(request.postDataJSON()).toMatchObject({ amount: '-30', adjustmentMode: 'Delta' });
  await expect(dialog).toBeHidden();

  const row = page.locator('tbody tr', { hasText: walletName });
  await expect(row).toHaveCount(1);
  await expect(row.locator('td').nth(4)).toHaveText('Изменение на сумму');
  expect(await balanceOn(page, walletId, isoDateDaysAgo(12))).toBe(70);

  // A stated balance on top of it: the correction row appears, the adjustment row stays editable (has "···").
  expect(await setStatedBalance(page, { walletName, date: isoDateDaysAgo(12), amount: '90' })).toBe(201);
  await expect(page.locator('tbody tr')).toHaveCount(2);
  await expect(page.locator('tbody tr.is-system')).toHaveCount(1);
  await expect(page.locator('tbody tr:not(.is-system)').getByRole('button', { name: /Действия с операцией/ })).toBeVisible();
  expect(await balanceOn(page, walletId, isoDateDaysAgo(12))).toBe(90);
});
