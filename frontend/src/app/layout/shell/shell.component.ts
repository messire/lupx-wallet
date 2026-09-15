import { ChangeDetectionStrategy, Component, ElementRef, HostListener, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ButtonComponent } from '@shared/ui/button/button.component';

interface NavItem {
  path: string;
  label: string;
}

/**
 * Shared shell for the authenticated part of the app: a sidebar with the brand,
 * navigation between sections, and a logout button (docs/design/ui-kit.md §4,
 * sidebar layout ≥1100px). Feature sections mount into a nested <router-outlet>
 * (see app.routes.ts).
 *
 * Below 1100px the sidebar is hidden and shown as a dropdown panel via the
 * topbar's "Меню" button instead — see shell.component.scss.
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

  /**
   * The mobile dropdown panel is meaningless once the sidebar breakpoint (1100px,
   * shell.component.scss) takes over — without this, resizing/rotating back to
   * desktop while the menu is open leaves `menuOpen()` stuck `true` behind the
   * now-hidden sidebar/backdrop. Just clears the flag — no focus move, this isn't
   * a user dismissal.
   */
  @HostListener('window:resize')
  onWindowResize(): void {
    if (this.menuOpen() && window.innerWidth >= 1100) {
      this.menuOpen.set(false);
    }
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
