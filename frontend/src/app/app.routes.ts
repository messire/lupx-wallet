import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: '',
    loadComponent: () => import('./features/wallets/wallets.component').then((m) => m.WalletsComponent),
    canActivate: [authGuard],
  },
  { path: '**', redirectTo: '' },
];
