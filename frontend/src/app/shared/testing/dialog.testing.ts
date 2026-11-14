/**
 * jsdom does not implement `HTMLDialogElement.showModal`/`close` (see dialog.component.spec.ts). Call in a
 * `beforeEach` of a spec that opens an `app-dialog`; returns the function that restores the originals.
 */
export function stubNativeDialog(): () => void {
  const proto = HTMLDialogElement.prototype as unknown as Record<string, unknown>;
  const originals = { showModal: proto['showModal'], close: proto['close'] };
  proto['showModal'] = function (this: HTMLDialogElement) {
    this.setAttribute('open', '');
  };
  proto['close'] = function (this: HTMLDialogElement) {
    if (this.hasAttribute('open')) {
      this.removeAttribute('open');
      this.dispatchEvent(new Event('close'));
    }
  };
  return () => {
    Object.assign(proto, originals);
  };
}
