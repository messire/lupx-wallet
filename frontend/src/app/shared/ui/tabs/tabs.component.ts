import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

export interface TabDefinition {
  id: string;
  label: string;
}

let nextTabsId = 0;

/**
 * A tablist for switching panels within a single location (docs/design/ui-kit.md
 * §5 "Tabs"): tablist/tab roles, arrow/Home/End key handling, roving tabindex,
 * a visible focus outline. Route transitions use regular navigation links —
 * this component is not meant for navigating between pages (see ui-kit: "do not
 * mix the two").
 *
 * The component does not render the content panels itself — it does not know
 * what should be inside them. The caller links `selectedId` to its own
 * `<div role="tabpanel">` elements (id = `{tabsId}-panel-{tab.id}`,
 * `aria-labelledby` = `{tabsId}-tab-{tab.id}`), or marks them with the
 * `appTabPanel` directive from tab-panel.directive.ts for automatic ARIA
 * markup/hiding.
 */
@Component({
  selector: 'app-tabs',
  standalone: true,
  templateUrl: './tabs.component.html',
  styleUrl: './tabs.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TabsComponent {
  readonly tabs = input.required<readonly TabDefinition[]>();
  readonly selectedId = input<string | null>(null);
  readonly ariaLabel = input<string | null>(null);

  readonly selectedIdChange = output<string>();

  readonly tabsId = `app-tabs-${nextTabsId++}`;

  readonly activeId = computed(() => this.selectedId() ?? this.tabs()[0]?.id ?? null);

  tabElementId(id: string): string {
    return `${this.tabsId}-tab-${id}`;
  }

  panelElementId(id: string): string {
    return `${this.tabsId}-panel-${id}`;
  }

  select(id: string): void {
    if (id !== this.activeId()) {
      this.selectedIdChange.emit(id);
    }
  }

  onKeydown(event: KeyboardEvent, index: number): void {
    const items = this.tabs();
    if (items.length === 0) {
      return;
    }
    let nextIndex: number | null = null;
    switch (event.key) {
      case 'ArrowRight':
        nextIndex = (index + 1) % items.length;
        break;
      case 'ArrowLeft':
        nextIndex = (index - 1 + items.length) % items.length;
        break;
      case 'Home':
        nextIndex = 0;
        break;
      case 'End':
        nextIndex = items.length - 1;
        break;
      default:
        return;
    }
    event.preventDefault();
    const next = items[nextIndex];
    this.select(next.id);
    queueMicrotask(() => {
      document.getElementById(this.tabElementId(next.id))?.focus();
    });
  }
}
