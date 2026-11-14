import { test, expect, login } from './fixtures';

/**
 * The dropdown trigger (`app-select`, ADR-0009 §6) keeps the field look — arrow image, 44px right padding, field
 * background — and its open list is the themed panel, not the browser's grey native popup. Real browser computed
 * styles only: jsdom does not run the cascade the way a real engine does.
 */
test('a dropdown keeps its arrow, padding and field background, and opens a themed list', async ({ page }) => {
  await login(page);
  await page.goto('/wallets');
  await page.getByRole('button', { name: 'Добавить кошелек' }).click();

  const trigger = page.locator('app-select[formcontrolname="walletTypeId"]').getByRole('combobox');
  await expect(trigger).toBeVisible();

  const computed = await trigger.evaluate((el) => {
    const cs = getComputedStyle(el);
    return { backgroundImage: cs.backgroundImage, backgroundColor: cs.backgroundColor, paddingRight: cs.paddingRight };
  });
  expect(computed.backgroundImage).toContain('data:image/svg+xml');
  expect(computed.paddingRight).toBe('44px');
  expect(computed.backgroundColor).toBe('rgba(17, 22, 60, 0.58)'); // --lw-field

  await trigger.click();
  const panel = page.locator('.app-anchored-panel');
  await expect(panel).toBeVisible();
  // --lw-surface-raised, and the panel is in the top layer (Popover API), so no card or dialog can clip it.
  await expect(panel).toHaveCSS('background-color', 'rgb(52, 57, 102)');
  expect(await panel.evaluate((el) => el.matches(':popover-open'))).toBe(true);
  await expect(page.getByRole('option').last()).toHaveText('+ добавить новый');
});

test('a readonly text input still gets the readonly background and text cursor', async ({ page }) => {
  await login(page);
  await page.goto('/wallets');
  // FieldComponent's (ViewEncapsulation.None) styles are only injected into the
  // document once a real `<app-field>` has actually rendered somewhere on the
  // page — opening the create-wallet form does that; without it the synthetic
  // input below would be checked against a document that never loaded those
  // rules at all, which is a gap in the test, not in the app.
  await page.getByRole('button', { name: 'Добавить кошелек' }).click();
  await expect(page.locator('input[formcontrolname="name"]')).toBeVisible();

  // No template currently renders a readonly input — inject one using the app's
  // own class to confirm the fix didn't regress the rule it narrowed.
  const computed = await page.evaluate(() => {
    const input = document.createElement('input');
    input.className = 'app-field__input';
    input.type = 'text';
    input.readOnly = true;
    input.value = 'x';
    document.body.appendChild(input);
    const cs = getComputedStyle(input);
    const result = { isReadOnly: input.matches(':read-only'), backgroundColor: cs.backgroundColor, cursor: cs.cursor };
    input.remove();
    return result;
  });

  expect(computed.isReadOnly).toBe(true);
  expect(computed.backgroundColor).toBe('rgba(25, 30, 69, 0.86)'); // --lw-glass-strong
  expect(computed.cursor).toBe('text');
});
