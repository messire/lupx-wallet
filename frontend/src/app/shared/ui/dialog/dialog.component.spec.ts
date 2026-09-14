import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DialogComponent } from './dialog.component';

/**
 * jsdom does not implement `HTMLDialogElement.prototype.showModal`/`close` at all
 * (see node_modules/jsdom/lib/jsdom/living/nodes/HTMLDialogElement-impl.js — it is an
 * empty subclass of HTMLElement; only the `open` content attribute is reflected).
 * There is no native modal semantics, focus trap, or `::backdrop` in this test
 * environment. To exercise DialogComponent's open/close/focus-restoration wiring we
 * install a minimal polyfill for this spec file only (restored afterwards).
 *
 * This means these tests verify that DialogComponent calls the right native APIs at
 * the right times and reacts correctly to the native `cancel`/`close` events — NOT
 * that the browser's real focus trap or backdrop dismissal behaves correctly. That
 * part cannot be verified in jsdom and remains a manual verification item.
 */
let originalShowModal: unknown;
let originalClose: unknown;

beforeAll(() => {
  const proto = HTMLDialogElement.prototype as unknown as Record<string, unknown>;
  originalShowModal = proto['showModal'];
  originalClose = proto['close'];
  proto['showModal'] = function (this: HTMLDialogElement) {
    this.setAttribute('open', '');
  };
  proto['close'] = function (this: HTMLDialogElement) {
    this.removeAttribute('open');
    this.dispatchEvent(new Event('close'));
  };
});

afterAll(() => {
  const proto = HTMLDialogElement.prototype as unknown as Record<string, unknown>;
  proto['showModal'] = originalShowModal;
  proto['close'] = originalClose;
});

describe('DialogComponent', () => {
  let fixture: ComponentFixture<DialogComponent>;
  let component: DialogComponent;
  let dialogEl: HTMLDialogElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DialogComponent],
    }).compileComponents();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  function create(open: boolean): void {
    fixture = TestBed.createComponent(DialogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('titleText', 'Подтверждение');
    fixture.componentRef.setInput('open', open);
    fixture.detectChanges();
    dialogEl = fixture.nativeElement.querySelector('dialog') as HTMLDialogElement;
  }

  it('calls showModal on the native dialog when open is true from the start', () => {
    const showModalSpy = vi.spyOn(HTMLDialogElement.prototype, 'showModal');

    create(true);

    expect(showModalSpy).toHaveBeenCalled();
    expect(dialogEl.hasAttribute('open')).toBe(true);
  });

  it('does not call showModal when open is false from the start', () => {
    const showModalSpy = vi.spyOn(HTMLDialogElement.prototype, 'showModal');

    create(false);

    expect(showModalSpy).not.toHaveBeenCalled();
    expect(dialogEl.hasAttribute('open')).toBe(false);
  });

  it('calls showModal when the open input flips from false to true', () => {
    create(false);
    const showModalSpy = vi.spyOn(HTMLDialogElement.prototype, 'showModal');

    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();

    expect(showModalSpy).toHaveBeenCalledTimes(1);
  });

  it('calls close on the native dialog when the open input flips from true to false', () => {
    create(true);
    const closeSpy = vi.spyOn(HTMLDialogElement.prototype, 'close');

    fixture.componentRef.setInput('open', false);
    fixture.detectChanges();

    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('emits dismissRequested when the native dialog fires "cancel" (Escape/backdrop)', () => {
    create(true);
    const emitted: Event[] = [];
    component.dismissRequested.subscribe((event) => emitted.push(event));

    const cancelEvent = new Event('cancel', { cancelable: true });
    dialogEl.dispatchEvent(cancelEvent);

    expect(emitted).toEqual([cancelEvent]);
  });

  it('emits closed and restores focus to the element focused before the dialog opened', () => {
    const trigger = document.createElement('button');
    document.body.appendChild(trigger);
    trigger.focus();
    expect(document.activeElement).toBe(trigger);

    create(true);
    let closedEmitted = false;
    component.closed.subscribe(() => {
      closedEmitted = true;
    });

    dialogEl.close();

    expect(closedEmitted).toBe(true);
    expect(document.activeElement).toBe(trigger);

    trigger.remove();
  });

  it('closes the native dialog on component destroy if it is still open', () => {
    create(true);
    const closeSpy = vi.spyOn(HTMLDialogElement.prototype, 'close');

    fixture.destroy();

    expect(closeSpy).toHaveBeenCalledTimes(1);
  });
});
