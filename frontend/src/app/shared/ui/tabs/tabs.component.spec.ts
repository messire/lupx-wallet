import { Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TabDefinition, TabsComponent } from './tabs.component';
import { TabPanelDirective } from './tab-panel.directive';

/**
 * Uses signal `input()`s (set via `fixture.componentRef.setInput(...)`) rather than plain
 * mutable fields: in this project's zoneless setup (no `zone.js`, no
 * `provideZonelessChangeDetection` — see `frontend/src/main.ts`/`app.config.ts`), mutating a
 * plain host field and calling `detectChanges()` a second time does not reliably re-push the
 * new value into a nested `OnPush` child's signal inputs (verified with an isolated repro
 * outside `TabsComponent`/`FieldComponent` — an environment characteristic, not a component
 * bug). `componentRef.setInput` goes through Angular's supported test API for driving signal
 * inputs and works reliably regardless.
 */
@Component({
  standalone: true,
  imports: [TabsComponent, TabPanelDirective],
  template: `
    <app-tabs [tabs]="tabs()" [selectedId]="selectedId()">
      <div appTabPanel="a" class="panel-a">Content A</div>
      <div appTabPanel="b" class="panel-b">Content B</div>
    </app-tabs>
  `,
})
class TabsHostComponent {
  readonly tabs = input<TabDefinition[]>([
    { id: 'a', label: 'A' },
    { id: 'b', label: 'B' },
  ]);
  readonly selectedId = input('a');
}

describe('TabsComponent', () => {
  let fixture: ComponentFixture<TabsComponent>;
  let component: TabsComponent;

  const tabs: TabDefinition[] = [
    { id: 'a', label: 'A' },
    { id: 'b', label: 'B' },
    { id: 'c', label: 'C' },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TabsComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(TabsComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('tabs', tabs);
    fixture.detectChanges();
  });

  function tabButtons(): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('button[role="tab"]'));
  }

  it('defaults the active tab to the first tab when selectedId is not set', () => {
    expect(component.activeId()).toBe('a');
  });

  it('emits selectedIdChange when a non-active tab is clicked', () => {
    const emitted: string[] = [];
    component.selectedIdChange.subscribe((id) => emitted.push(id));

    tabButtons()[1].click();

    expect(emitted).toEqual(['b']);
  });

  it('does not emit selectedIdChange when clicking the already active tab', () => {
    const emitted: string[] = [];
    component.selectedIdChange.subscribe((id) => emitted.push(id));

    tabButtons()[0].click();

    expect(emitted).toEqual([]);
  });

  it('marks only the active tab with tabIndex 0 (roving tabindex)', () => {
    const buttons = tabButtons();
    expect(buttons[0].tabIndex).toBe(0);
    expect(buttons[1].tabIndex).toBe(-1);
    expect(buttons[2].tabIndex).toBe(-1);
  });

  it('sets aria-selected on the active tab and aria-controls pointing at its panel id', () => {
    const buttons = tabButtons();
    expect(buttons[0].getAttribute('aria-selected')).toBe('true');
    expect(buttons[1].getAttribute('aria-selected')).toBe('false');
    expect(buttons[0].getAttribute('aria-controls')).toBe(component.panelElementId('a'));
    expect(buttons[0].id).toBe(component.tabElementId('a'));
  });

  it('ArrowRight moves selection to the next tab, wrapping from the last tab to the first', () => {
    fixture.componentRef.setInput('selectedId', 'c');
    fixture.detectChanges();
    const emitted: string[] = [];
    component.selectedIdChange.subscribe((id) => emitted.push(id));

    component.onKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), 2);

    expect(emitted).toEqual(['a']);
  });

  it('ArrowLeft moves selection to the previous tab, wrapping from the first tab to the last', () => {
    const emitted: string[] = [];
    component.selectedIdChange.subscribe((id) => emitted.push(id));

    component.onKeydown(new KeyboardEvent('keydown', { key: 'ArrowLeft' }), 0);

    expect(emitted).toEqual(['c']);
  });

  it('Home jumps to the first tab', () => {
    fixture.componentRef.setInput('selectedId', 'c');
    fixture.detectChanges();
    const emitted: string[] = [];
    component.selectedIdChange.subscribe((id) => emitted.push(id));

    component.onKeydown(new KeyboardEvent('keydown', { key: 'Home' }), 2);

    expect(emitted).toEqual(['a']);
  });

  it('End jumps to the last tab', () => {
    const emitted: string[] = [];
    component.selectedIdChange.subscribe((id) => emitted.push(id));

    component.onKeydown(new KeyboardEvent('keydown', { key: 'End' }), 0);

    expect(emitted).toEqual(['c']);
  });

  it('ignores unrelated keys without emitting or preventing default', () => {
    const emitted: string[] = [];
    component.selectedIdChange.subscribe((id) => emitted.push(id));
    const event = new KeyboardEvent('keydown', { key: 'Tab' });
    const preventDefault = vi.spyOn(event, 'preventDefault');

    component.onKeydown(event, 0);

    expect(emitted).toEqual([]);
    expect(preventDefault).not.toHaveBeenCalled();
  });

  it('moves DOM focus to the newly selected tab after arrow/Home/End navigation', async () => {
    component.onKeydown(new KeyboardEvent('keydown', { key: 'End' }), 0);
    await Promise.resolve();

    expect(document.activeElement).toBe(tabButtons()[2]);
  });

  describe('appTabPanel content projection', () => {
    let hostFixture: ComponentFixture<TabsHostComponent>;

    beforeEach(async () => {
      await TestBed.resetTestingModule();
      await TestBed.configureTestingModule({
        imports: [TabsHostComponent],
      }).compileComponents();

      hostFixture = TestBed.createComponent(TabsHostComponent);
      hostFixture.detectChanges();
    });

    it('renders projected appTabPanel content nested inside app-tabs and links it to its tab via ARIA ids', () => {
      const host = hostFixture.nativeElement as HTMLElement;
      const tabA = host.querySelector('button[role="tab"]') as HTMLButtonElement;
      const panelA = host.querySelector('.panel-a') as HTMLElement;
      const panelB = host.querySelector('.panel-b') as HTMLElement;

      expect(panelA).not.toBeNull();
      expect(panelA.getAttribute('role')).toBe('tabpanel');
      expect(panelA.id).toBe(tabA.getAttribute('aria-controls'));
      expect(panelA.getAttribute('aria-labelledby')).toBe(tabA.id);
      expect(panelA.hidden).toBe(false);
      expect(panelB.hidden).toBe(true);
    });

    it('toggles hidden panels when the host updates selectedId', () => {
      hostFixture.componentRef.setInput('selectedId', 'b');
      hostFixture.detectChanges();

      const host = hostFixture.nativeElement as HTMLElement;
      const panelA = host.querySelector('.panel-a') as HTMLElement;
      const panelB = host.querySelector('.panel-b') as HTMLElement;

      expect(panelA.hidden).toBe(true);
      expect(panelB.hidden).toBe(false);
    });
  });
});
