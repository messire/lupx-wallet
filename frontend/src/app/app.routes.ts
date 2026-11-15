import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

/**
 * The single file with the application's routes — all
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
        // UI Operations (UC-11...UC-17, UC-24).
        path: 'operations',
        loadComponent: () =>
          import('./features/operations/pages/operations-page/operations-page.component').then(
            (m) => m.OperationsComponent,
          ),
      },
      {
        // UI Transfers.
        path: 'transfers',
        loadComponent: () =>
          import('./features/transfers/pages/transfers-page/transfers-page.component').then((m) => m.TransfersComponent),
      },
      {
        // UI Planning — planned transactions and their occurrences (#9, #13, ADR-0010 §6).
        path: 'planning',
        loadComponent: () =>
          import('./features/planning/pages/planning-page/planning-page.component').then((m) => m.PlanningComponent),
      },
      {
        // UI Budgeting — monthly budgets and expense categories (#11, #13, ADR-0010 §5).
        path: 'budgets',
        loadComponent: () =>
          import('./features/budgeting/pages/budgets-page/budgets-page.component').then((m) => m.BudgetsComponent),
      },
      {
        // UI Debts — debts and the creditor dictionary (#6, #13, ADR-0010 §3).
        path: 'debts',
        loadComponent: () => import('./features/debts/pages/debts-page/debts-page.component').then((m) => m.DebtsComponent),
      },
      {
        // UI Debts — loans with a payment schedule (#8, #13, ADR-0010 §3).
        path: 'loans',
        loadComponent: () => import('./features/debts/pages/loans-page/loans-page.component').then((m) => m.LoansComponent),
      },
      {
        // UI Funds — funds and reservations (#7, #13, ADR-0010 §4).
        path: 'funds',
        loadComponent: () => import('./features/funds/pages/funds-page/funds-page.component').then((m) => m.FundsComponent),
      },
      {
        // UI BalanceHistory (UC-18, UC-20, UC-23).
        path: 'balance-history',
        loadComponent: () =>
          import('./features/balance-history/pages/balance-history-page/balance-history-page.component').then(
            (m) => m.BalanceHistoryComponent,
          ),
      },
      {
        // UI ExchangeRates (UC-08…UC-10).
        path: 'exchange-rates',
        loadComponent: () =>
          import('./features/exchange-rates/pages/exchange-rates-page/exchange-rates-page.component').then(
            (m) => m.ExchangeRatesComponent,
          ),
      },
      {
        // UI Reporting (UC-19, UC-21).
        path: 'reporting',
        loadComponent: () =>
          import('./features/reporting/pages/reporting-page/reporting-page.component').then((m) => m.ReportingComponent),
      },
      {
        // Free funds and upcoming payments (#12, ADR-0010 §7, В9).
        path: 'free-funds',
        loadComponent: () =>
          import('./features/reporting/pages/free-funds-page/free-funds-page.component').then((m) => m.FreeFundsComponent),
      },
      {
        // Cash-flow forecast (#10, ADR-0010 §7).
        path: 'forecast',
        loadComponent: () => import('./features/reporting/pages/forecast-page/forecast-page.component').then((m) => m.ForecastComponent),
      },
      {
        // UI for managing reference data (UC-26, UC-27 + deactivate/delete; UC-28 — the "Валюты" tab:
        // add from the available list, remove from the active ones).
        // The wallet-type quick-add mini-form stays in features/wallets and reuses the
        // same ReferenceDataApiService (a wallet's currency is picked from the available list, no quick-add).
        path: 'reference-data',
        loadComponent: () =>
          import('./features/reference-data/pages/reference-data-page/reference-data-page.component').then(
            (m) => m.ReferenceDataComponent,
          ),
      },
      {
        // UI Audit (UC-25).
        path: 'audit',
        loadComponent: () =>
          import('./features/audit/pages/audit-page/audit-page.component').then((m) => m.AuditComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
