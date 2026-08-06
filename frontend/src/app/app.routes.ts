import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

/**
 * Единственный файл с маршрутами приложения (docs/PROGRESS.md, W0.2) — все
 * lazy-маршруты будущих фич прописаны здесь заранее, чтобы последующие
 * UI-задачи (Operations/Transfers, ReferenceData, BalanceHistory, ExchangeRates,
 * Reporting, Audit, действия над кошельком) трогали только свою папку
 * `features/<name>/` и не редактировали этот файл. Незаполненные разделы
 * указывают на общую заглушку UnderConstructionComponent — соответствующая
 * задача заменяет только свою запись `loadComponent`.
 */
export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell.component').then((m) => m.ShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'wallets' },
      {
        path: 'wallets',
        loadComponent: () => import('./features/wallets/wallets.component').then((m) => m.WalletsComponent),
      },
      {
        // W1.3 — UI Operations/Transfers (UC-11…UC-17, UC-24).
        path: 'operations',
        data: { title: 'Операции' },
        loadComponent: () => import('./features/operations/operations.component').then((m) => m.OperationsComponent),
      },
      {
        // W1.3 — UI Operations/Transfers.
        path: 'transfers',
        data: { title: 'Переводы' },
        loadComponent: () => import('./features/transfers/transfers.component').then((m) => m.TransfersComponent),
      },
      {
        // W2.3 — UI BalanceHistory (UC-18, UC-20, UC-23).
        path: 'balance-history',
        data: { title: 'История баланса' },
        loadComponent: () =>
          import('./features/balance-history/balance-history.component').then((m) => m.BalanceHistoryComponent),
      },
      {
        // W2.4 — UI ExchangeRates (UC-08…UC-10).
        path: 'exchange-rates',
        data: { title: 'Курсы валют' },
        loadComponent: () =>
          import('./features/exchange-rates/exchange-rates.component').then((m) => m.ExchangeRatesComponent),
      },
      {
        // W3.1 — UI Reporting (UC-19, UC-21).
        path: 'reporting',
        data: { title: 'Отчёт' },
        loadComponent: () => import('./features/reporting/reporting.component').then((m) => m.ReportingComponent),
      },
      {
        // W1.4 — UI управления справочниками (UC-26, UC-27 + деактивация/удаление).
        // Мини-формы быстрого добавления справочников остаются в features/wallets
        // и переиспользуют тот же ReferenceDataService.
        path: 'reference-data',
        data: { title: 'Справочники' },
        loadComponent: () =>
          import('./features/reference-data/reference-data.component').then((m) => m.ReferenceDataComponent),
      },
      {
        // W3.2 — UI Audit (UC-25).
        path: 'audit',
        data: { title: 'Аудит' },
        loadComponent: () => import('./features/audit/audit.component').then((m) => m.AuditComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
