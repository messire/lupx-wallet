import { ChangeDetectionStrategy, Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ButtonComponent } from '@shared/ui/button/button.component';

interface NavItem {
  path: string;
  label: string;
}

/**
 * Shared shell for the authenticated part of the app: a header with the brand,
 * navigation between sections, and a logout button. Feature sections mount into
 * a nested <router-outlet> (see app.routes.ts).
 *
 * On narrow screens the navigation (8 items) collapses into a dropdown menu —
 * there is only enough width for the brand and the hamburger button, see
 * shell.component.scss.
 */
@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, RouterOutlet, ButtonComponent],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShellComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly menuOpen = signal(false);

  /**
   * The hamburger button stays a native <button> (not app-button) so that
   * programmatically restoring focus after closing the mobile menu (ui-kit.md §5
   * "Navigation and topbar") is reliable: `.focus()` is guaranteed to work on a
   * focusable native element, whereas the host element of the presentational
   * app-button is not focusable on its own.
   */
  private readonly menuToggleButton = viewChild<ElementRef<HTMLButtonElement>>('menuToggleButton');

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

  toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  closeMenu(): void {
    if (!this.menuOpen()) {
      return;
    }

    this.menuOpen.set(false);
    this.menuToggleButton()?.nativeElement.focus();
  }

  logout(): void {
    this.closeMenu();
    this.authService.logout();
    this.router.navigateByUrl('/login');
  }
}
