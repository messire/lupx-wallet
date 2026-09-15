import { test, expect, login } from './fixtures';

/**
 * Regression check for a bug where `.app-field__input:read-only:not(:disabled)`
 * matched native <select> elements too (a <select> has no readonly attribute, but
 * every element that isn't an editable text control still matches CSS :read-only —
 * see field.component.scss) and its `background` shorthand silently cleared the
 * arrow image set by `select.app-field__input`. Real browser computed styles only —
 * jsdom does not implement CSS :read-only matching against a live cascade the way a
 * real engine does, so this can't be caught by a component unit test.
 */
test('an enabled select keeps its arrow image, correct padding, and field background', async ({ page }) => {
  await login(page);
  await page.goto('/wallets');
  await page.getByRole('button', { name: 'Добавить кошелек' }).click();

  const select = page.locator('select[formcontrolname="walletTypeId"]');
  await expect(select).toBeVisible();

  const computed = await select.evaluate((el) => {
    const cs = getComputedStyle(el);
    return {
      isReadOnly: el.matches(':read-only'),
      backgroundImage: cs.backgroundImage,
      backgroundColor: cs.backgroundColor,
      paddingRight: cs.paddingRight,
    };
  });

  // The select does match :read-only (an inherent CSS quirk, not the bug) — the
  // fix is that the arrow/padding survive that regardless.
  expect(computed.isReadOnly).toBe(true);
  expect(computed.backgroundImage).not.toBe('none');
  expect(computed.backgroundImage).toContain('data:image/svg+xml');
  expect(computed.paddingRight).toBe('44px');
  // --lw-field, not --lw-glass-strong/--lw-disabled-bg — confirms the readonly/
  // disabled rules' `background` shorthand no longer overrides it via the select.
  expect(computed.backgroundColor).toBe('rgba(17, 22, 60, 0.58)');
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
