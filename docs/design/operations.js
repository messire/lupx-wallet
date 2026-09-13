/* In-memory operation fixtures. Monetary values remain strings throughout. */
const operationWallets = { daily: 'Ежедневные расходы', savings: 'Накопления', travel: 'Путешествия' };
const typeNames = { Income: 'Доход', Expense: 'Расход', Adjustment: 'Корректировка', Transfer: 'Перевод' };
const operationRows = [
  { id: 'o1', date: '2026-09-14', wallet: 'savings', type: 'Income', amount: '1200.00' },
  { id: 'o2', date: '2026-09-14', wallet: 'daily', type: 'Expense', amount: '64.80' },
  { id: 'o3', date: '2026-09-14', wallet: 'daily', type: 'Expense', amount: '4.50' },
  { id: 'o4', date: '2026-09-14', wallet: 'daily', type: 'Adjustment', amount: '-25.00', mode: 'Delta' },
  { id: 'o5', date: '2026-09-14', wallet: 'daily', type: 'Transfer', amount: '250.00', transfer: true },
  { id: 'o6', date: '2026-09-13', wallet: 'daily', type: 'Expense', amount: '19.90' },
  { id: 'o7', date: '2026-09-11', wallet: 'travel', type: 'Income', amount: '400.00' },
  { id: 'o8', date: '2026-09-10', wallet: 'travel', type: 'Adjustment', amount: '2000.00', mode: 'Absolute' }
];
const operationSample = document.querySelector('#operations-sample');
const operationBody = document.querySelector('#operations-body');
const filtersForm = document.querySelector('#filters');
const operationForm = document.querySelector('#operation-form');
const operationDialog = document.querySelector('#operation-dialog');
const actionsDialog = document.querySelector('#row-actions-dialog');
const deleteDialog = document.querySelector('#delete-operation-dialog');
const saveOperation = document.querySelector('#save-operation');
let appliedFilters = { wallet: '', type: '', from: '', to: '' };
let operationLimit = 6;
let selectedOperation;
let editingOperationId = null;
let operationBaseline;
let operationTimer;
let operationBusy = false;
let newOperationId = 9;

const dateLabel = value => value.split('-').reverse().join('.');
const operationName = wallet => operationSample.value === 'long' && wallet === 'daily' ? 'Ежедневные расходы семьи и покупки для дома' : operationWallets[wallet];
function effectiveRows() {
  return operationRows.map(row => operationSample.value === 'long' && row.id === 'o1' ? { ...row, amount: '1234567890.123456789' } : row);
}
function amountLabel(row) {
  const sign = row.type === 'Income' ? '+' : row.type === 'Expense' ? '−' : '';
  return sign + specimen.formatAmount(row.amount) + ' EUR';
}
function addCell(row, text, className = '') {
  const cell = document.createElement('td');
  cell.textContent = text;
  cell.className = className;
  row.append(cell);
  return cell;
}
function renderOperations() {
  const state = operationSample.value;
  const waiting = ['loading', 'error', 'empty'].includes(state);
  const rows = waiting ? [] : effectiveRows().filter(row => (!appliedFilters.wallet || row.wallet === appliedFilters.wallet) && (!appliedFilters.type || row.type === appliedFilters.type) && (!appliedFilters.from || row.date >= appliedFilters.from) && (!appliedFilters.to || row.date <= appliedFilters.to));
  const shown = rows.slice(0, operationLimit);
  operationBody.replaceChildren();
  shown.forEach(row => {
    const tr = document.createElement('tr');
    tr.dataset.id = row.id;
    addCell(tr, dateLabel(row.date));
    addCell(tr, operationName(row.wallet));
    const typeCell = addCell(tr, typeNames[row.type]);
    if (row.transfer) { const note = document.createElement('span'); note.className = 'transfer-note'; note.textContent = 'Часть перевода'; typeCell.append(note); }
    addCell(tr, amountLabel(row), 'amount ' + (row.type === 'Income' ? 'positive' : row.type === 'Expense' ? 'negative' : ''));
    addCell(tr, row.mode === 'Absolute' ? 'Новый баланс' : row.mode === 'Delta' ? 'Изменение на сумму' : '—', 'metadata');
    const actions = addCell(tr, '');
    const button = document.createElement('button');
    button.className = 'row-action';
    button.textContent = '···';
    button.setAttribute('aria-label', `Действия: ${typeNames[row.type]}, ${operationName(row.wallet)}, ${amountLabel(row)}, ${dateLabel(row.date)}`);
    button.addEventListener('click', () => openRowActions(row));
    actions.append(button);
    operationBody.append(tr);
  });
  const hasFilter = Object.values(appliedFilters).some(Boolean);
  const noMatch = !waiting && !shown.length && hasFilter;
  document.querySelector('#operation-table').hidden = !shown.length;
  document.querySelector('#operations-state').hidden = Boolean(shown.length);
  document.querySelector('#operations-state').classList.toggle('loading', state === 'loading');
  document.querySelector('#operations-list').setAttribute('aria-busy', String(state === 'loading'));
  document.querySelector('#row-count').textContent = waiting ? state === 'loading' ? 'Загрузка…' : state === 'error' ? 'Данные недоступны' : 'Нет операций' : `Показано: ${shown.length}`;
  document.querySelector('#load-more').hidden = shown.length >= rows.length;
  const stateCopy = state === 'loading' ? ['Загружаем операции', 'Подождите немного.'] : state === 'error' ? ['Не удалось загрузить операции', 'Повторите попытку. Выбранные фильтры сохранены.'] : noMatch ? ['По этим условиям ничего не найдено', 'Измените период или сбросьте фильтры.'] : ['Операций пока нет', 'Добавьте первую операцию, чтобы начать учёт.'];
  document.querySelector('#operations-state-title').textContent = stateCopy[0];
  document.querySelector('#operations-state-description').textContent = stateCopy[1];
  document.querySelector('#retry-operations').hidden = state !== 'error';
  document.querySelector('#clear-empty-filter').hidden = !noMatch;
  specimen.announce(shown.length ? `Показано операций: ${shown.length}` : stateCopy[0]);
}
filtersForm.addEventListener('submit', event => {
  event.preventDefault();
  const data = Object.fromEntries(new FormData(filtersForm));
  const invalid = data.from && data.to && data.from > data.to;
  specimen.error(document.querySelector('#filter-from'), 'filter-error', invalid ? 'Начало периода не может быть позже его окончания.' : '');
  document.querySelector('#filter-to').setAttribute('aria-invalid', String(Boolean(invalid)));
  if (invalid) { document.querySelector('#filter-from').focus(); return; }
  appliedFilters = data;
  operationLimit = 6;
  renderOperations();
});
function clearOperationFilters() {
  filtersForm.reset();
  appliedFilters = { wallet: '', type: '', from: '', to: '' };
  operationLimit = 6;
  specimen.error(document.querySelector('#filter-from'), 'filter-error', '');
  document.querySelector('#filter-to').setAttribute('aria-invalid', 'false');
  renderOperations();
}
document.querySelector('#reset-filters').addEventListener('click', clearOperationFilters);
document.querySelector('#clear-empty-filter').addEventListener('click', () => { clearOperationFilters(); document.querySelector('#filter-wallet').focus(); });
document.querySelector('#load-more').addEventListener('click', () => { operationLimit += 6; renderOperations(); });
document.querySelector('#retry-operations').addEventListener('click', () => { operationSample.value = 'default'; renderOperations(); document.querySelector('#filter-wallet').focus(); });
operationSample.addEventListener('change', () => { operationLimit = 6; renderOperations(); });

function describeRow(row) { return `${typeNames[row.type]} · ${amountLabel(row)} · ${operationName(row.wallet)} · ${dateLabel(row.date)}`; }
function openRowActions(row) {
  selectedOperation = row;
  document.querySelector('#row-description').textContent = describeRow(row);
  document.querySelector('#edit-row').hidden = Boolean(row.transfer);
  document.querySelector('#delete-row').hidden = Boolean(row.transfer);
  document.querySelector('#transfer-row').hidden = !row.transfer;
  document.querySelector('#delete-row').disabled = row.date !== '2026-09-14';
  document.querySelector('#delete-rule').textContent = row.transfer ? 'Часть перевода изменяется и удаляется только через сам перевод.' : row.date < '2026-09-14' ? 'Операцию за прошлую дату можно редактировать, но нельзя удалить.' : '';
  actionsDialog.showModal();
}
document.querySelector('#edit-row').addEventListener('click', () => { actionsDialog.close(); openOperationForm(selectedOperation); });
document.querySelector('#delete-row').addEventListener('click', () => {
  if (selectedOperation.transfer || selectedOperation.date !== '2026-09-14') return;
  actionsDialog.close();
  document.querySelector('#delete-description').textContent = describeRow(selectedOperation);
  deleteDialog.returnValue = '';
  deleteDialog.showModal();
});
deleteDialog.addEventListener('close', () => {
  if (deleteDialog.returnValue !== 'delete') return;
  const index = operationRows.findIndex(row => row.id === selectedOperation.id);
  if (index >= 0 && !selectedOperation.transfer && selectedOperation.date === '2026-09-14') operationRows.splice(index, 1);
  renderOperations();
  specimen.message('operations-feedback', 'Операция удалена из макета.');
  document.querySelector('#operations-feedback').focus();
});
function openUnavailableSection(name) { actionsDialog.close(); document.querySelector('#screen-note-title').textContent = name; document.querySelector('#screen-note').showModal(); }
document.querySelector('#transfer-row').addEventListener('click', () => openUnavailableSection('Переводы'));
document.querySelector('#audit-row').addEventListener('click', () => openUnavailableSection('История изменений'));

function updateAdjustment() {
  const adjustment = document.querySelector('#operation-type').value === 'Adjustment';
  document.querySelector('#adjustment-choice').hidden = !adjustment;
  document.querySelector('#adjustment-choice').disabled = !adjustment;
  const absolute = adjustment && operationForm.elements.mode.value === 'Absolute';
  document.querySelector('#operation-amount-label').textContent = absolute ? 'Новый баланс *' : 'Сумма *';
  document.querySelector('#operation-amount-help').textContent = adjustment ? 'Допустимы положительные и отрицательные значения. Разделитель — точка.' : 'Положительное число с точкой, например 1250.50.';
}
operationForm.addEventListener('change', updateAdjustment);
function openOperationForm(row = null) {
  editingOperationId = row?.id ?? null;
  operationForm.reset();
  document.querySelector('#operation-fields').disabled = false;
  operationBusy = false;
  saveOperation.disabled = false;
  saveOperation.textContent = row ? 'Сохранить' : 'Создать';
  operationForm.setAttribute('aria-busy', 'false');
  document.querySelector('#operation-form-title').textContent = row ? 'Редактировать операцию' : 'Новая операция';
  if (row) {
    operationForm.elements.wallet.value = row.wallet;
    operationForm.elements.type.value = row.type;
    operationForm.elements.amount.value = row.amount;
    operationForm.elements.date.value = row.date;
    operationForm.elements.mode.value = row.mode || 'Delta';
  }
  operationForm.querySelectorAll('.field-error').forEach(error => { error.hidden = true; });
  operationForm.querySelectorAll('[aria-invalid]').forEach(input => input.setAttribute('aria-invalid', 'false'));
  specimen.message('operation-save-error', '');
  updateAdjustment();
  operationBaseline = specimen.fingerprint(operationForm);
  operationDialog.showModal();
}
document.querySelector('#add-operation').addEventListener('click', () => openOperationForm());
function validateOperation(onlyInput = null) {
  const data = Object.fromEntries(new FormData(operationForm));
  const validAmount = specimen.amount(data.amount ?? '');
  const positiveAmount = validAmount && !data.amount.trim().startsWith('-') && /[1-9]/.test(data.amount);
  const checks = [
    ['operation-wallet', data.wallet ? '' : 'Выберите кошелёк.'],
    ['operation-type', data.type ? '' : 'Выберите тип операции.'],
    ['operation-amount', !validAmount ? 'Введите число с точкой, например 1250.50.' : data.type !== 'Adjustment' && !positiveAmount ? 'Для дохода и расхода сумма должна быть больше нуля.' : ''],
    ['operation-date', !data.date ? 'Укажите дату.' : data.date < '2026-09-01' || data.date > '2026-09-14' ? 'Дата должна быть в периоде с 01.09.2026 по 14.09.2026.' : '']
  ];
  let valid = true;
  checks.forEach(([id, message]) => { if ((!onlyInput || onlyInput.id === id) && !specimen.error(document.getElementById(id), id + '-error', message)) valid = false; });
  return valid;
}
operationForm.addEventListener('focusout', event => { if (event.target.matches('[required]')) validateOperation(event.target); });
operationForm.addEventListener('change', event => {
  if (event.target.matches('[required]')) validateOperation(event.target);
  const amountInput = document.querySelector('#operation-amount');
  if (event.target.id === 'operation-type' && amountInput.getAttribute('aria-invalid') === 'true') validateOperation(amountInput);
});
operationForm.addEventListener('submit', event => {
  event.preventDefault();
  if (operationBusy) return;
  if (!validateOperation()) { operationForm.querySelector('[aria-invalid=true]')?.focus(); return; }
  const data = Object.fromEntries(new FormData(operationForm));
  operationBusy = true;
  document.querySelector('#operation-fields').disabled = true;
  saveOperation.disabled = true;
  saveOperation.textContent = 'Сохранение…';
  operationForm.setAttribute('aria-busy', 'true');
  specimen.message('operation-save-error', '');
  operationTimer = setTimeout(() => {
    operationBusy = false;
    document.querySelector('#operation-fields').disabled = false;
    saveOperation.disabled = false;
    saveOperation.textContent = editingOperationId ? 'Сохранить' : 'Создать';
    operationForm.setAttribute('aria-busy', 'false');
    updateAdjustment();
    if (document.querySelector('#operation-save-state').value === 'error') { specimen.message('operation-save-error', 'Не удалось сохранить операцию. Введённые значения сохранены — попробуйте ещё раз.'); saveOperation.focus(); return; }
    const row = { id: editingOperationId || 'o' + newOperationId++, wallet: data.wallet, type: data.type, amount: data.amount.trim(), date: data.date, ...(data.type === 'Adjustment' ? { mode: data.mode } : {}) };
    const index = operationRows.findIndex(item => item.id === editingOperationId);
    if (index >= 0) operationRows[index] = row; else operationRows.unshift(row);
    operationSample.value = 'default';
    operationDialog.close();
    renderOperations();
    specimen.message('operations-feedback', 'Операция сохранена в макете.');
    document.querySelector('#operations-feedback').focus();
  }, 600);
});
function cancelOperation() {
  const discard = () => { clearTimeout(operationTimer); operationBusy = false; operationDialog.close(); };
  if (specimen.fingerprint(operationForm) !== operationBaseline) specimen.confirmDiscard(discard); else discard();
}
document.querySelector('#cancel-operation').addEventListener('click', cancelOperation);
operationDialog.addEventListener('cancel', event => { event.preventDefault(); cancelOperation(); });
const queryWallet = new URLSearchParams(location.search).get('wallet');
if (Object.hasOwn(operationWallets, queryWallet)) { document.querySelector('#filter-wallet').value = queryWallet; appliedFilters.wallet = queryWallet; }
renderOperations();
