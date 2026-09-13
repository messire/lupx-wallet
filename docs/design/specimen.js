/* Shared local-only helpers for documentation screens. */
const specimen = {
  announce(text) { document.querySelector('#announcement').textContent = text; },
  error(input, id, message) {
    const output = document.getElementById(id);
    output.textContent = message;
    output.hidden = !message;
    input.setAttribute('aria-invalid', String(Boolean(message)));
    return !message;
  },
  amount(value) { return /^-?\d+(\.\d+)?$/.test(value.trim()); },
  formatAmount(value) {
    const [integer, fraction] = value.split('.');
    return integer.replace(/\B(?=(\d{3})+(?!\d))/g, ' ') + (fraction === undefined ? '' : ',' + fraction);
  },
  fingerprint(form) { return JSON.stringify([...new FormData(form)]); },
  confirmDiscard(callback) {
    const dialog = document.querySelector('#discard-dialog');
    dialog.returnValue = '';
    dialog.addEventListener('close', () => { if (dialog.returnValue === 'discard') callback(); }, { once: true });
    dialog.showModal();
  },
  message(id, text) { const element = document.getElementById(id); element.textContent = text; element.hidden = !text; }
};
const specimenSidebar = document.querySelector('.sidebar');
const specimenMenu = document.querySelector('.menu-toggle');
function closeSpecimenMenu(focus = false) {
  specimenSidebar.classList.remove('is-open');
  specimenMenu.setAttribute('aria-expanded', 'false');
  if (focus) specimenMenu.focus();
}
specimenMenu.addEventListener('click', () => {
  const open = specimenSidebar.classList.toggle('is-open');
  specimenMenu.setAttribute('aria-expanded', String(open));
  if (open) specimenSidebar.querySelector('.current').focus();
});
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && specimenSidebar.classList.contains('is-open') && !document.querySelector('dialog[open]')) closeSpecimenMenu(true);
});
window.matchMedia('(min-width:1100px)').addEventListener('change', () => closeSpecimenMenu());
document.querySelectorAll('[data-preview]').forEach(button => button.addEventListener('click', () => {
  document.querySelector('#screen-note-title').textContent = button.dataset.preview;
  document.querySelector('#screen-note').showModal();
}));
