import { DestroyRef, Directive, ElementRef, afterNextRender, inject, input } from '@angular/core';

/** Gap between the anchor and the panel, px (ui-kit spacing step `--lw-space-1`). */
const GAP = 4;
/** Minimal margin kept between the panel and the viewport edge, px. */
const VIEWPORT_MARGIN = 8;

/**
 * Floating panel of a dropdown control (`app-select`, `app-currency-select`, `app-date-input`) — the list or
 * calendar that opens under a field.
 *
 * The panel is promoted to the browser top layer through the Popover API (`popover="manual"`) and positioned
 * with `position: fixed` against its anchor's `getBoundingClientRect()`. An absolutely positioned panel was
 * clipped by every scrolling ancestor (the dialog body is `overflow-y: auto`) and painted under neighbouring
 * cards (each `app-card` is its own stacking context) — the top layer is above both, including an open modal
 * `<dialog>`. Where the Popover API is missing (jsdom in unit tests) the panel stays an ordinary fixed element.
 *
 * Opens downward; flips upward when the space below is smaller than the panel and there is more space above.
 * The width matches the anchor (`matchWidth`, default) or is the panel's own (`matchWidth=false`, e.g. the
 * calendar). Follows the anchor on scroll and resize while it exists — the host is rendered only while open.
 */
@Directive({
  selector: '[appAnchoredPanel]',
  standalone: true,
  host: {
    popover: 'manual',
    class: 'app-anchored-panel',
  },
})
export class AnchoredPanelDirective {
  /** Element the panel is attached to (the trigger/field). */
  readonly appAnchoredPanel = input.required<HTMLElement>();
  /** Panel width equals the anchor width; otherwise the panel keeps its own width and is aligned to the anchor's left edge. */
  readonly matchWidth = input(true);

  private readonly panel = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  constructor() {
    const reposition = () => this.reposition();
    afterNextRender(() => {
      const panel = this.panel as HTMLElement & { showPopover?: () => void };
      if (typeof panel.showPopover === 'function') {
        try {
          panel.showPopover();
        } catch {
          // Already shown or detached — positioning below still applies.
        }
      }
      this.reposition();
      window.addEventListener('scroll', reposition, true);
      window.addEventListener('resize', reposition);
    });

    inject(DestroyRef).onDestroy(() => {
      window.removeEventListener('scroll', reposition, true);
      window.removeEventListener('resize', reposition);
      const panel = this.panel as HTMLElement & { hidePopover?: () => void };
      if (typeof panel.hidePopover === 'function') {
        try {
          panel.hidePopover();
        } catch {
          // Not shown — nothing to hide.
        }
      }
    });
  }

  private reposition(): void {
    const anchor = this.appAnchoredPanel();
    if (!anchor.isConnected) {
      return;
    }
    const rect = anchor.getBoundingClientRect();
    const style = this.panel.style;
    const viewportWidth = document.documentElement.clientWidth || window.innerWidth;
    const viewportHeight = window.innerHeight;

    if (this.matchWidth()) {
      style.width = `${rect.width}px`;
    }
    const panelWidth = this.matchWidth() ? rect.width : this.panel.offsetWidth;
    const left = Math.max(
      VIEWPORT_MARGIN,
      Math.min(rect.left, viewportWidth - panelWidth - VIEWPORT_MARGIN),
    );
    style.left = `${left}px`;

    const panelHeight = this.panel.offsetHeight;
    const spaceBelow = viewportHeight - rect.bottom - GAP - VIEWPORT_MARGIN;
    const spaceAbove = rect.top - GAP - VIEWPORT_MARGIN;
    const openUp = panelHeight > spaceBelow && spaceAbove > spaceBelow;

    if (openUp) {
      style.top = 'auto';
      style.bottom = `${viewportHeight - rect.top + GAP}px`;
      style.setProperty('--app-panel-available', `${Math.max(spaceAbove, 120)}px`);
    } else {
      style.bottom = 'auto';
      style.top = `${rect.bottom + GAP}px`;
      style.setProperty('--app-panel-available', `${Math.max(spaceBelow, 120)}px`);
    }
    this.panel.dataset['placement'] = openUp ? 'top' : 'bottom';
  }
}
