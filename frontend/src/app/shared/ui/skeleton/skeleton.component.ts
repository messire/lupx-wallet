import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * A static (non-animated) skeleton shape for content (docs/design/ui-kit.md §5
 * "Empty, loading, error, feedback"): "a static skeleton shaped like the content".
 * Purely decorative — has no text of its own and is marked aria-hidden; the
 * accessible "Loading…" label is provided by `app-loading-state`, which wraps
 * one or more `app-skeleton`s.
 */
@Component({
  selector: 'app-skeleton',
  standalone: true,
  template: `<span class="app-skeleton" aria-hidden="true"></span>`,
  styleUrl: './skeleton.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[style.width]': 'width()',
    '[style.height]': 'height()',
    '[style.borderRadius]': "circle() ? '50%' : null",
  },
})
export class SkeletonComponent {
  readonly width = input('100%');
  readonly height = input('12px');
  readonly circle = input(false);
}
