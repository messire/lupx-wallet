import { Directive, computed, inject, input } from '@angular/core';
import { TabsComponent } from './tabs.component';

/**
 * Optional helper for an `app-tabs` content panel: applies id, role="tabpanel",
 * aria-labelledby, and hidden based on the current `activeId` of the parent
 * `app-tabs`, found via DI against the render hierarchy (content projected from
 * a parent template is taken into account — see field-control.directive.ts for
 * the same trick). Does not set any visual styles — panel content layout stays
 * the caller's responsibility.
 */
@Directive({
  selector: '[appTabPanel]',
  standalone: true,
  host: {
    role: 'tabpanel',
    tabindex: '0',
    '[id]': 'tabs.panelElementId(tabId())',
    '[attr.aria-labelledby]': 'tabs.tabElementId(tabId())',
    '[hidden]': '!isActive()',
  },
})
export class TabPanelDirective {
  protected readonly tabs = inject(TabsComponent);

  readonly appTabPanel = input.required<string>();
  protected readonly tabId = computed(() => this.appTabPanel());
  protected readonly isActive = computed(() => this.tabs.activeId() === this.tabId());
}
