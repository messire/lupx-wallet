import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

/**
 * UI kit v0.2 button (docs/design/ui-kit.md §5 "Buttons and links"). A presentational
 * component — it knows nothing about domain operations, only about the display
 * variant/state; the caller decides what to do on click (the native click event
 * bubbles from the inner <button> to the host element, so (click) on <app-button>
 * works without a separate output()).
 *
 * Loading is shown on top of the original content (`visibility: hidden` instead of
 * removing it from the layout flow), so switching to the loading state does not
 * change the button's width — the ui-kit requirement that "width must not jump".
 *
 * Icon-only: `iconOnly` gives a square 44x44 px area with no inner padding;
 * `ariaLabel` is required in that case since there is no visible text.
 */
@Component({
  selector: 'app-button',
  standalone: true,
  templateUrl: './button.component.html',
  styleUrl: './button.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[class.app-button-host--full]': 'fullWidth()',
  },
})
export class ButtonComponent {
  readonly variant = input<ButtonVariant>('secondary');
  readonly iconOnly = input(false);
  /** Stretches the button to the full width of its parent (e.g. a form submit button). */
  readonly fullWidth = input(false);
  readonly type = input<'button' | 'submit' | 'reset'>('button');
  readonly disabled = input(false);
  readonly loading = input(false);
  /** Text that replaces the content while loading, e.g. "Saving…". */
  readonly loadingText = input<string | null>(null);
  /** Required when `iconOnly` is set — the accessible action name for assistive technologies. */
  readonly ariaLabel = input<string | null>(null);
}
