import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type BadgeTone = 'neutral' | 'accent' | 'success' | 'danger' | 'warning';

/**
 * Status badge (docs/design/ui-kit.md §5 "Badge / chip"): communicates a state
 * (e.g. "Primary", "Archived", "Income", "Expense" — the text is passed via
 * <ng-content>, the component does not hardcode the wording) and does not look
 * like a button — not clickable, no hover/focus states.
 */
@Component({
  selector: 'app-badge',
  standalone: true,
  template: `<span class="app-badge app-badge--{{ tone() }}"><ng-content></ng-content></span>`,
  styleUrl: './badge.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BadgeComponent {
  readonly tone = input<BadgeTone>('neutral');
}
