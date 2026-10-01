# Browser smoke tests (Playwright)

Runs against the real running app — a live Angular dev server talking to the
real .NET API and a real Postgres — not a mocked frontend. The only
intentional exception is `wallets.spec.ts`'s save-error scenario, which
intercepts just the one request that must fail, so that scenario is
deterministic instead of depending on a specific business-rule conflict.

## Running locally

Requires the full stack already running (see repo root README for Docker
Compose, or run backend + `ng serve` from each project's own README) and a
database with the auth password from `backend/README.md`
(`ChangeMe123!`, dev-only).

```bash
npx playwright install --with-deps chromium   # once
npm run e2e
```

Set `E2E_BASE_URL` if the frontend isn't on `http://localhost:4200`.

**Network access to the real Frankfurter service is required** (`https://api.frankfurter.dev/v2/`). The
list of available currencies (ADR-0004 §7) is empty on a fresh database and is filled only by a rates
refresh; `createWallet` and the currency helpers in `fixtures.ts` (`ensureAvailableCurrency`,
`ensureCurrency`) call `POST /exchange-rates/refresh` when the needed code is missing, and fail with an
explicit message if it is still absent afterwards (no network). A wallet's currency is chosen by code in
the currency combobox — there is no quick-add for currencies.

Login is limited to 5 requests/minute and every test logs in: run spec files one at a time with a pause
of about a minute between them.

## What each file covers

- `wallets.spec.ts` — the four scenarios required by the architecture audit
  (docs/PROGRESS.md, "CI и браузерные проверки"): create a wallet, a failed
  save shows a readable error and keeps the entered data, delete-confirmation
  dialog removes the wallet, Escape dismisses the dialog without deleting and
  returns focus to the button that opened it.
- `reference-data.spec.ts` — the currencies tab: add a currency from the available ones, then remove it
  from the active ones (with the confirmation dialog).
- `wallets.spec.ts` additionally checks that a wallet in a not-yet-active currency activates it.
- `exchange-rates.spec.ts` additionally checks that without a primary wallet "Обновить сейчас" still loads
  the list of available currencies.
- `responsive-visual.spec.ts` — not a pass/fail suite; captures the Wallets
  screen at 360/768/1440px and at 200% zoom with a long Russian wallet name
  and a large amount, for manual visual review (screenshots saved under
  `e2e/screenshots/manual-review/`, gitignored — see docs/PROGRESS.md for the
  reviewed set from this audit).

## Known limitations

- `document.documentElement.style.zoom` approximates browser page-zoom; it is
  not a pixel-identical reproduction of a real Ctrl/Cmd+Plus zoom (which
  Playwright cannot drive without a raw CDP session), but it exercises the
  same CSS (`clamp()`/container queries/`rem`) a real zoom would.
- Tests share one backend/database (`fullyParallel: false`, one worker) —
  each wallet name includes `Date.now()` so scenarios don't collide with
  leftover data from a previous run against the same database.
