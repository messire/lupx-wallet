import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  effect,
  input,
  output,
  viewChild,
} from '@angular/core';

let nextDialogId = 0;

export type DialogSize = 'sm' | 'md' | 'lg';

/**
 * Modal / form panel built on the native <dialog> (docs/design/ui-kit.md §5
 * "Modal / form panel"). The browser gives focus trapping and modality for free
 * via `showModal()`; the component is responsible for: opening/closing driven by
 * the `open` input, restoring focus to the initiator after close, and forwarding
 * the Escape/backdrop event (`cancel`) outward as `dismissRequested`, so the
 * caller can stop the close when there are unsaved changes (`event.preventDefault()`).
 *
 * Size: sm => 440px (short confirmation), md => 560px (simple form),
 * lg => wide panel (layout left to the screen, see ui-kit §5).
 */
@Component({
  selector: 'app-dialog',
  standalone: true,
  templateUrl: './dialog.component.html',
  styleUrl: './dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DialogComponent implements AfterViewInit, OnDestroy {
  private readonly dialogRef = viewChild.required<ElementRef<HTMLDialogElement>>('dialogEl');

  readonly open = input(false);
  readonly titleText = input.required<string>();
  readonly size = input<DialogSize>('sm');

  /** Escape or a backdrop click — the caller can cancel the close via event.preventDefault(). */
  readonly dismissRequested = output<Event>();
  /** The dialog has actually closed (after cancel/close, or after the `open` input becomes false). */
  readonly closed = output<void>();

  readonly titleId = `app-dialog-title-${nextDialogId++}`;

  private previouslyFocused: HTMLElement | null = null;
  private viewReady = false;

  constructor() {
    effect(() => {
      const shouldBeOpen = this.open();
      if (!this.viewReady) {
        return;
      }
      const el = this.dialogRef().nativeElement;
      if (shouldBeOpen && !el.open) {
        this.previouslyFocused = document.activeElement as HTMLElement | null;
        el.showModal();
      } else if (!shouldBeOpen && el.open) {
        el.close();
      }
    });
  }

  ngAfterViewInit(): void {
    this.viewReady = true;
    if (this.open()) {
      this.previouslyFocused = document.activeElement as HTMLElement | null;
      this.dialogRef().nativeElement.showModal();
    }
  }

  ngOnDestroy(): void {
    const el = this.dialogRef()?.nativeElement;
    if (el?.open) {
      el.close();
    }
  }

  onCancel(event: Event): void {
    this.dismissRequested.emit(event);
  }

  onClose(): void {
    this.closed.emit();
    this.previouslyFocused?.focus();
    this.previouslyFocused = null;
  }
}
