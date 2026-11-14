const transferRows = [
  { id: 1, date: '2026-09-15', source: 'daily', target: 'savings', amount: '250.00' },
  { id: 2, date: '2026-09-15', source: 'savings', target: 'travel', amount: '400.00' },
  { id: 3, date: '2026-09-14', source: 'daily', target: 'travel', amount: '150.50' },
  { id: 4, date: '2026-09-12', source: 'savings', target: 'daily', amount: '100.00' },
  { id: 5, date: '2026-09-10', source: 'daily', target: 'savings', amount: '75.00' },
  { id: 6, date: '2026-09-09', source: 'travel', target: 'daily', amount: '20.00' }
];
let transferFilter = '', transferLimit = 5, selectedTransfer, transferBusy = false, transferTimer, transferBaseline;
const transferForm = demo.el('transfer-form');
const transferDialog = demo.el('transfer-dialog');
function describeTransfer(row) { return `${demo.name(row.source)} → ${demo.name(row.target)} · ${demo.money(row.amount, demo.wallets[row.source].currency)} · ${demo.date(row.date)}`; }
function renderTransfers() {
  const state = demo.state();
  const rows = ['empty','loading','error'].includes(state) ? [] : transferRows.filter(row => !transferFilter || row.source === transferFilter || row.target === transferFilter);
  const shown = rows.slice(0, transferLimit);
  demo.el('transfer-rows').replaceChildren();
  shown.forEach(original => {
    const row = state === 'long' && original.id === 1 ? { ...original, amount: '1234567890.123456789' } : original;
    const action = document.createElement('button'); action.className = 'row-action'; action.textContent = '···';
    action.setAttribute('aria-label', 'Действия: ' + describeTransfer(row));
    action.addEventListener('click', () => {
      selectedTransfer = original;
      demo.el('transfer-description').textContent = describeTransfer(row);
      demo.el('ask-delete-transfer').disabled = row.date !== demo.today;
      demo.el('transfer-delete-rule').textContent = row.date !== demo.today ? 'Перевод за прошлую дату удалить нельзя.' : 'Удаление затронет обе связанные операции.';
      demo.el('transfer-actions').showModal();
    });
    demo.row('transfer', [demo.date(row.date), demo.name(row.source), demo.name(row.target), { text: demo.money(row.amount, demo.wallets[row.source].currency), className: 'amount' }, { node: action }]);
  });
  demo.listState('transfer', state, shown.length > 0, transferFilter ? 'По этому кошельку переводов нет' : 'Переводов пока нет', transferFilter ? 'Выберите другой кошелёк или сбросьте фильтр.' : 'Добавьте перевод, чтобы учесть перемещение между кошельками.');
  demo.more('transfer', shown.length, rows.length);
  demo.el('transfer-count').textContent = ['loading','error'].includes(state) ? '—' : `Показано: ${shown.length}`;
}
demo.el('transfer-filter').addEventListener('submit', event => { event.preventDefault(); transferFilter = demo.el('transfer-wallet').value; transferLimit = 5; renderTransfers(); });
demo.el('reset-transfer-filter').addEventListener('click', () => { transferFilter = ''; demo.el('transfer-filter').reset(); transferLimit = 5; renderTransfers(); });
demo.el('transfer-more').addEventListener('click', () => { transferLimit += 5; renderTransfers(); demo.el('transfer-table').focus(); });
demo.el('transfer-retry').addEventListener('click', () => { demo.el('demo-state').value = 'default'; renderTransfers(); demo.el('transfer-wallet').focus(); });
demo.el('demo-state').addEventListener('change', () => { transferLimit = 5; renderTransfers(); });
function updateTarget() {
  const source = demo.wallets[demo.el('source').value];
  const target = demo.el('target'); target.replaceChildren(new Option(source ? 'Выберите получателя' : 'Сначала выберите источник', ''));
  const eligible = source ? Object.entries(demo.wallets).filter(([id, wallet]) => id !== demo.el('source').value && wallet.currency === source.currency) : [];
  eligible.forEach(([id, wallet]) => target.add(new Option(wallet.name + ' · ' + wallet.currency, id)));
  target.disabled = !eligible.length;
  demo.el('target-help').textContent = source && !eligible.length ? 'Нет другого кошелька в ' + source.currency + '. Перевод между разными валютами недоступен.' : source ? 'Доступны кошельки в ' + source.currency + '.' : '';
  demo.el('transfer-currency').textContent = source?.currency || '—';
  specimen.error(target, 'target-error', ''); updateTransferDate();
}
function updateTransferDate() {
  const source = demo.wallets[demo.el('source').value], target = demo.wallets[demo.el('target').value];
  const min = [source?.start, target?.start].filter(Boolean).sort().at(-1) || '2026-09-01';
  demo.el('transfer-date').min = min;
  demo.el('date-help').textContent = `С ${demo.date(min)} по ${demo.date(demo.today)} — период учёта обоих кошельков.`;
}
demo.el('source').addEventListener('change', updateTarget);
demo.el('target').addEventListener('change', updateTransferDate);
demo.el('add-transfer').addEventListener('click', () => {
  transferForm.reset(); demo.clearErrors(transferForm); demo.el('transfer-fields').disabled = false;
  demo.el('save-transfer').disabled = false; demo.el('save-transfer').textContent = 'Создать перевод';
  transferForm.setAttribute('aria-busy','false'); transferBusy = false; updateTarget();
  transferBaseline = specimen.fingerprint(transferForm); transferDialog.showModal();
});
function validateTransfer() {
  const source = demo.el('source'), target = demo.el('target'), amount = demo.el('transfer-amount'), date = demo.el('transfer-date');
  const value = amount.value.trim();
  const checks = [
    [source,'source-error',source.value ? '' : 'Выберите кошелёк-источник.'],
    [target,'target-error',target.value && source.value !== target.value && demo.wallets[source.value]?.currency === demo.wallets[target.value]?.currency ? '' : 'Выберите другой кошелёк той же валюты.'],
    [amount,'amount-error',specimen.amount(value) && !value.startsWith('-') && /[1-9]/.test(value) ? '' : 'Введите положительную сумму с точкой.'],
    [date,'date-error',date.value && date.value >= date.min && date.value <= demo.today ? '' : 'Выберите дату в периоде учёта обоих кошельков.']
  ];
  checks.forEach(([input,id,message]) => specimen.error(input,id,message));
  const invalid = checks.find(([, ,message]) => message);
  if (invalid) { (invalid[0].disabled ? source : invalid[0]).focus(); return false; } return true;
}
transferForm.addEventListener('submit', event => {
  event.preventDefault(); if (transferBusy || !validateTransfer()) return;
  const row = { id: Math.max(0,...transferRows.map(r=>r.id))+1, source: demo.el('source').value, target: demo.el('target').value, amount: demo.el('transfer-amount').value.trim(), date: demo.el('transfer-date').value };
  transferBusy = true; demo.el('transfer-fields').disabled = true; demo.el('save-transfer').disabled = true; demo.el('save-transfer').textContent = 'Сохранение…'; transferForm.setAttribute('aria-busy','true'); specimen.message('transfer-save-error','');
  transferTimer = setTimeout(() => {
    transferBusy = false; demo.el('transfer-fields').disabled = false; demo.el('save-transfer').disabled = false; demo.el('save-transfer').textContent = 'Создать перевод'; transferForm.setAttribute('aria-busy','false');
    if (demo.fails()) { specimen.message('transfer-save-error','Не удалось сохранить перевод. Введённые данные сохранены — повторите попытку.'); demo.el('save-transfer').focus(); return; }
    transferRows.unshift(row); demo.el('demo-state').value = 'default'; transferDialog.close(); renderTransfers();
    specimen.message('transfer-feedback','Перевод создан в макете.'); demo.el('transfer-feedback').focus();
  }, 500);
});
function cancelTransfer() { const discard = () => { clearTimeout(transferTimer); transferBusy = false; transferDialog.close(); }; if (specimen.fingerprint(transferForm) !== transferBaseline) specimen.confirmDiscard(discard); else discard(); }
demo.el('cancel-transfer').addEventListener('click', cancelTransfer);
transferDialog.addEventListener('cancel', event => { event.preventDefault(); cancelTransfer(); });
demo.el('ask-delete-transfer').addEventListener('click', () => {
  if (selectedTransfer.date !== demo.today) return;
  demo.el('transfer-actions').close(); demo.el('transfer-delete-description').textContent = describeTransfer(selectedTransfer);
  specimen.message('transfer-delete-error',''); demo.el('transfer-delete').showModal();
});
demo.el('cancel-delete-transfer').addEventListener('click', () => demo.el('transfer-delete').close());
demo.el('confirm-delete-transfer').addEventListener('click', () => {
  if (selectedTransfer.date !== demo.today) return;
  if (demo.fails()) { specimen.message('transfer-delete-error','Не удалось удалить перевод. Данные не изменены. Повторите попытку.'); return; }
  const index = transferRows.findIndex(row => row.id === selectedTransfer.id); if (index >= 0) transferRows.splice(index,1);
  demo.el('transfer-delete').close(); renderTransfers(); specimen.message('transfer-feedback','Перевод удалён из макета.'); demo.el('transfer-feedback').focus();
});
renderTransfers();
