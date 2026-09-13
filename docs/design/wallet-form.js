/* Wallet create/edit specimen, including optional fields already in the API. */
const walletForm = document.querySelector('#wallet-form');
const walletFields = document.querySelector('#wallet-fields');
const walletMode = document.querySelector('#form-mode');
const walletState = document.querySelector('#form-state');
const walletSave = document.querySelector('#save-wallet');
const typeSelect = document.querySelector('#wallet-type');
const currencySelect = document.querySelector('#wallet-currency');
let walletTimer;
let walletBaseline;
let walletBusy = false;
const queryMode = new URLSearchParams(location.search).get('mode');
const queryWallet = new URLSearchParams(location.search).get('wallet');
const sampleWalletNames = { daily: 'Ежедневные расходы', savings: 'Накопления', travel: 'Путешествия' };
walletMode.value = ['edit', 'primary'].includes(queryMode) ? queryMode : 'create';

function updateCurrencyLabel() { document.querySelector('#currency-suffix').textContent = currencySelect.value || '—'; }
currencySelect.addEventListener('change', updateCurrencyLabel);

function walletValidation(onlyInput = null) {
  let valid = true;
  const checks = [
    ['wallet-name', 'wallet-name-error', document.querySelector('#wallet-name').value.trim() ? '' : 'Введите название кошелька.'],
    ['wallet-type', 'type-error', typeSelect.value ? '' : 'Выберите тип кошелька.'],
    ['display-order', 'display-order-error', document.querySelector('#display-order').validity.valid ? '' : 'Введите целое число.']
  ];
  if (walletMode.value === 'create') {
    checks.push(['wallet-currency', 'currency-error', currencySelect.value ? '' : 'Выберите валюту.']);
    checks.push(['initial-balance', 'initial-balance-error', specimen.amount(document.querySelector('#initial-balance').value) ? '' : 'Введите число с точкой, например 1250.50.']);
    checks.push(['start-date', 'start-date-error', document.querySelector('#start-date').value ? '' : 'Укажите дату начала учёта.']);
  }
  checks.forEach(([id, errorId, message]) => { if ((!onlyInput || onlyInput.id === id) && !specimen.error(document.getElementById(id), errorId, message)) valid = false; });
  return valid;
}
function configureWalletMode() {
  clearTimeout(walletTimer);
  walletBusy = false;
  walletForm.reset();
  const editing = walletMode.value !== 'create';
  const walletKey = walletMode.value === 'primary' ? 'daily' : Object.hasOwn(sampleWalletNames, queryWallet) ? queryWallet : 'travel';
  document.querySelector('#wallet-name').value = editing ? sampleWalletNames[walletKey] : '';
  typeSelect.value = editing ? walletKey === 'savings' ? 'savings' : 'personal' : '';
  currencySelect.value = 'EUR';
  document.querySelector('#form-title').textContent = editing ? 'Редактировать кошелёк' : 'Новый кошелёк';
  document.title = document.querySelector('#form-title').textContent + ' — UI kit v0.3';
  document.querySelectorAll('.create-only').forEach(group => { group.hidden = editing; group.querySelectorAll('input,select,button').forEach(input => { input.disabled = editing; }); });
  document.querySelectorAll('.edit-only').forEach(group => { group.hidden = !editing; });
  document.querySelector('#advanced-fields').open = editing;
  document.querySelector('#primary-note').hidden = walletMode.value !== 'primary';
  walletState.value = 'default';
  updateCurrencyLabel();
  renderWalletState();
  walletBaseline = specimen.fingerprint(walletForm);
}
function renderWalletState() {
  clearTimeout(walletTimer);
  const state = walletState.value;
  walletBusy = state === 'saving';
  walletFields.disabled = walletBusy;
  walletSave.disabled = walletBusy || state.startsWith('reference-');
  walletSave.textContent = walletBusy ? 'Сохранение…' : walletMode.value === 'create' ? 'Создать кошелёк' : 'Сохранить';
  walletForm.setAttribute('aria-busy', String(walletBusy));
  const refsUnavailable = state.startsWith('reference-');
  typeSelect.disabled = refsUnavailable;
  currencySelect.disabled = refsUnavailable || walletMode.value !== 'create';
  document.querySelector('#save-type').disabled = refsUnavailable;
  document.querySelector('#save-currency').disabled = refsUnavailable;
  document.querySelector('#reference-feedback').hidden = !refsUnavailable;
  document.querySelector('#retry-references').hidden = state !== 'reference-error';
  specimen.message('reference-message', refsUnavailable ? state === 'reference-loading' ? 'Загружаем типы кошельков и валюты…' : 'Не удалось загрузить типы кошельков и валюты.' : '');
  document.querySelector('#include-total').disabled = walletMode.value === 'primary';
  document.querySelector('#include-total').checked = walletMode.value === 'primary' || document.querySelector('#include-total').checked;
  document.querySelectorAll('.field-error').forEach(error => { error.hidden = true; });
  document.querySelectorAll('[aria-invalid]').forEach(input => input.setAttribute('aria-invalid', 'false'));
  specimen.message('save-error', state === 'save-error' ? 'Не удалось сохранить кошелёк. Введённые данные сохранены — попробуйте ещё раз.' : '');
  specimen.message('save-success', state === 'success' ? 'Кошелёк сохранён в макете.' : '');
  if (state === 'invalid') { document.querySelector('#wallet-name').value = ''; document.querySelector('#initial-balance').value = '12,3,4'; walletValidation(); }
  if (state === 'long') { document.querySelector('#wallet-name').value = 'Ежедневные расходы семьи и покупки для дома'; document.querySelector('#initial-balance').value = '1234557900.123456789'; }
  specimen.announce(state === 'saving' ? 'Сохранение…' : state === 'save-error' ? 'Не удалось сохранить кошелёк' : 'Состояние формы обновлено');
}
walletMode.addEventListener('change', configureWalletMode);
walletState.addEventListener('change', renderWalletState);
document.querySelector('#retry-references').addEventListener('click', () => { walletState.value = 'default'; renderWalletState(); typeSelect.focus(); });
walletForm.addEventListener('focusout', event => { if (event.target.matches('[required],#display-order')) walletValidation(event.target); });
walletForm.addEventListener('change', event => { if (event.target.matches('[required],#display-order')) walletValidation(event.target); });
walletForm.addEventListener('submit', event => {
  event.preventDefault();
  if (walletBusy || walletSave.disabled) return;
  if (!walletValidation()) { walletForm.querySelector('[aria-invalid=true]')?.focus(); return; }
  const shouldFail = walletState.value === 'save-error';
  walletBusy = true;
  walletFields.disabled = true;
  walletSave.disabled = true;
  walletSave.textContent = 'Сохранение…';
  walletForm.setAttribute('aria-busy', 'true');
  specimen.message('save-error', '');
  walletTimer = setTimeout(() => {
    walletState.value = shouldFail ? 'save-error' : 'success';
    renderWalletState();
    if (!shouldFail) { walletBaseline = specimen.fingerprint(walletForm); document.querySelector('#save-success').focus(); }
    else walletSave.focus();
  }, 600);
});
function addReference(inputId, select, detailsId, errorId) {
  const input = document.getElementById(inputId);
  const value = input.value.trim();
  if (!specimen.error(input, errorId, value ? '' : 'Укажите значение.')) return;
  if (walletState.value === 'reference-error') { specimen.error(input, errorId, 'Не удалось добавить значение. Повторите загрузку справочников.'); return; }
  let option = [...select.options].find(item => item.value === value || item.text === value);
  if (!option) { option = new Option(input.tagName === 'SELECT' ? input.selectedOptions[0].text : value, value); select.add(option); }
  select.value = option.value;
  walletValidation(select);
  document.getElementById(detailsId).open = false;
  select.focus();
  updateCurrencyLabel();
  specimen.announce('Значение добавлено в макет');
}
document.querySelector('#save-type').addEventListener('click', () => addReference('new-type', typeSelect, 'add-type', 'new-type-error'));
document.querySelector('#save-currency').addEventListener('click', () => addReference('new-currency', currencySelect, 'add-currency', 'new-currency-error'));
document.querySelectorAll('a[href]').forEach(link => link.addEventListener('click', event => {
  if (link.getAttribute('href').startsWith('#') || specimen.fingerprint(walletForm) === walletBaseline) return;
  event.preventDefault();
  specimen.confirmDiscard(() => { location.href = link.href; });
}));
configureWalletMode();
