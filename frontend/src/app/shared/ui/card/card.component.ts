import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Glass surface (docs/design/ui-kit.md §2, §3, §5): a single glass layer
 * (`--lw-glass`, 24 px blur via `--lw-blur`), `--lw-shadow` shadow, a top light
 * edge (`--lw-border-light`), and a `--lw-panel-reflection` reflection.
 * The fallback to an opaque `--lw-surface` under `prefers-reduced-transparency`
 * is already built into the tokens themselves (frontend/src/styles.scss) — the
 * component does not override blur/transparency separately.
 *
 * `padded` enables the card's default inner padding (28 px desktop / 22 px
 * mobile); without it the component is just a visual surface for content that
 * lays itself out (e.g. a table inside a card, avoiding double padding).
 */
@Component({
  selector: 'app-card',
  standalone: true,
  template: `<div class="app-card" [class.app-card--padded]="padded()"><ng-content></ng-content></div>`,
  styleUrl: './card.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CardComponent {
  readonly padded = input(true);
}
