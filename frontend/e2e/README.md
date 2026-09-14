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

## What each file covers

- `wallets.spec.ts` — the four scenarios required by the architecture audit
  (docs/PROGRESS.md, "CI и браузерные проверки"): create a wallet, a failed
  save shows a readable error and keeps the entered data, delete-confirmation
  dialog removes the wallet, Escape dismisses the dialog without deleting and
  returns focus to the button that opened it.
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
