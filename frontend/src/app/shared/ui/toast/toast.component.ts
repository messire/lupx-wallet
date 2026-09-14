import { ChangeDetectionStrategy, Component, OnDestroy, effect, input, output } from '@angular/core';

export type ToastTone = 'success' | 'info' | 'error';

/**
 * A single toast notification (docs/design/ui-kit.md §5 "Empty, loading, error,
 * feedback"): `role="status"`/`aria-live="polite"`, auto-dismiss for success, not
 * for a critical error ("an important error stays until closed/resolved"). This
 * is only the presentational "capsule" for one toast — a queue of multiple
 * toasts, their on-screen positioning, and a global display service are not
 * implemented here (TODO for the slice where the first real toast caller shows up).
 *
 * Toast is not the only place a form error is reported — this component does not
 * replace `app-field`/`app-error-state`.
 */
@Component({
  selector: 'app-toast',
  standalone: true,
  templateUrl: './toast.component.html',
  styleUrl: './toast.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ToastComponent implements OnDestroy {
  readonly message = input.required<string>();
  readonly tone = input<ToastTone>('info');
  /** Auto-dismisses after 6 seconds; ignored for `tone === 'error'` regardless of the value. */
  readonly autoDismiss = input(true);

  readonly dismissed = output<void>();

  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      const message = this.message();
      const shouldAutoDismiss = this.autoDismiss() && this.tone() !== 'error';
      this.clearTimer();
      if (shouldAutoDismiss && message) {
        this.timer = setTimeout(() => this.dismissed.emit(), 6000);
      }
    });
  }

  ngOnDestroy(): void {
    this.clearTimer();
  }

  onDismissClick(): void {
    this.dismissed.emit();
  }

  private clearTimer(): void {
    if (this.timer !== undefined) {
      clearTimeout(this.timer);
      this.timer = undefined;
    }
  }
}
