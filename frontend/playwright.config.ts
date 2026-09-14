import { defineConfig, devices } from '@playwright/test';

/**
 * Browser smoke tests against the real running app (Angular dev server +
 * .NET API + Postgres) — not a mock. See e2e/README.md for what each
 * scenario covers and CI wiring in .github/workflows/ci.yml.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false, // scenarios share one seeded backend/DB — see e2e/README.md
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  workers: 1,
  reporter: [['html', { open: 'never' }], ['list']],
  outputDir: 'e2e/screenshots',
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:4200',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
