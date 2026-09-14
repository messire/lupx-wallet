import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Empty state for a list/filter (docs/design/ui-kit.md §5 "Empty, loading, error,
 * feedback"). Text is a component input, not hardcoded: the title/description
 * shown in the spec are example wording, not defaults defined here. The action
 * (e.g. a "Create…" or "Clear filters" button) is passed via <ng-content>.
 */
@Component({
  selector: 'app-empty-state',
  standalone: true,
  templateUrl: './empty-state.component.html',
  styleUrl: './empty-state.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmptyStateComponent {
  readonly title = input.required<string>();
  readonly description = input<string | null>(null);
}
