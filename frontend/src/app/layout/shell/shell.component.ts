import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

interface NavItem {
  path: string;
  label: string;
}

/**
 * Единая оболочка авторизованной части приложения: шапка с брендом, навигация
 * между разделами и кнопка выхода (ранее дублировалась внутри wallets.component).
 * Разделы фич монтируются в дочерний <router-outlet> (см. app.routes.ts).
 */
@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShellComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly navItems: NavItem[] = [
    { path: '/wallets', label: 'Кошельки' },
    { path: '/operations', label: 'Операции' },
    { path: '/transfers', label: 'Переводы' },
    { path: '/balance-history', label: 'История баланса' },
    { path: '/exchange-rates', label: 'Курсы валют' },
    { path: '/reporting', label: 'Отчёт' },
    { path: '/reference-data', label: 'Справочники' },
    { path: '/audit', label: 'Аудит' },
  ];

  logout(): void {
    this.authService.logout();
    this.router.navigateByUrl('/login');
  }
}
