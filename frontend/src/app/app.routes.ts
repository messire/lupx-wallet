import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

/**
 * The single file with the application's routes (docs/PROGRESS.md, W0.2) — all
 * feature lazy routes are declared here, so UI work on a specific feature only
 * touches its own `features/<name>/` folder and does not edit this file.
 */
export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login/pages/login-page/login-page.component').then((m) => m.LoginComponent),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell/shell.component').then((m) => m.ShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'wallets' },
      {
        path: 'wallets',
        loadComponent: () =>
          import('./features/wallets/pages/wallets-page/wallets-page.component').then((m) => m.WalletsComponent),
      },
      {
        // W1.3 — UI Operations/Transfers (UC-11...UC-17, UC-24).
        path: 'operations',
        loadComponent: () =>
          import('./features/operations/pages/operations-page/operations-page.component').then(
            (m) => m.OperationsComponent,
          ),
      },
      {
        // W1.3 — UI Operations/Transfers.
        path: 'transfers',
        loadComponent: () =>
          import('./features/transfers/pages/transfers-page/transfers-page.component').then((m) => m.TransfersComponent),
      },
      {
        // W2.3 — UI BalanceHistory (UC-18, UC-20, UC-23).
        path: 'balance-history',
        loadComponent: () =>
          import('./features/balance-history/pages/balance-history-page/balance-history-page.component').then(
            (m) => m.BalanceHistoryComponent,
          ),
      },
      {
        // W2.4 — UI ExchangeRates (UC-08…UC-10).
        path: 'exchange-rates',
        loadComponent: () =>
          import('./features/exchange-rates/pages/exchange-rates-page/exchange-rates-page.component').then(
            (m) => m.ExchangeRatesComponent,
          ),
      },
      {
        // W3.1 — UI Reporting (UC-19, UC-21).
        path: 'reporting',
        loadComponent: () =>
          import('./features/reporting/pages/reporting-page/reporting-page.component').then((m) => m.ReportingComponent),
      },
      {
        // W1.4 — UI for managing reference data (UC-26, UC-27 + deactivate/delete).
        // Quick-add mini-forms for reference data stay in features/wallets and
        // reuse the same ReferenceDataApiService.
        path: 'reference-data',
        loadComponent: () =>
          import('./features/reference-data/pages/reference-data-page/reference-data-page.component').then(
            (m) => m.ReferenceDataComponent,
          ),
      },
      {
        // W3.2 — UI Audit (UC-25).
        path: 'audit',
        loadComponent: () =>
          import('./features/audit/pages/audit-page/audit-page.component').then((m) => m.AuditComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
