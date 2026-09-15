const historyAmounts = {
  daily: ['2700.00','2625.00','2600.00','2700.00','2680.50','2530.00','2480.50'],
  savings: ['7500.00','7575.00','7575.00','7475.00','7900.00','8150.00','8000.00'],
  travel: ['1500.00','1500.00','1650.00','1650.00','1650.00','1800.00','2000.00'],
  usd: ['500.00','500.00','500.00','520.00','520.00','520.00','600.00']
};
let historyWallet = 'daily', historyLimit = 5, historyRange = { from: '2026-09-09', to: '2026-09-15' }, balanceDate = '2026-09-15';
let historyQueried = true, balanceQueried = true, historyRequestError = false, balanceRequestError = false;
function historyFixtures() {
  return historyAmounts[historyWallet].map((amount,i) => ({ date: '2026-09-' + String(9+i).padStart(2,'0'), amount: demo.state() === 'long' ? '1234567890.123456789' : amount }));
}
function renderBalance() {
  const state = demo.state(), wallet = demo.wallets[historyWallet];
  if (!wallet) return;
  const error = state === 'error' || balanceRequestError;
  const waiting = state === 'loading';
  const date = balanceDate || demo.today;
  const before = date < wallet.start;
  const match = historyFixtures().find(row => row.date === date);
  demo.el('balance-result').setAttribute('aria-busy',String(waiting));
  demo.el('balance-date-label').textContent = balanceQueried ? 'На ' + demo.date(date) : 'Баланс на выбранную дату';
  demo.el('balance-value').textContent = !balanceQueried || error || waiting || state === 'empty' ? '—' : before ? demo.money('0',wallet.currency) : match ? demo.money(match.amount,wallet.currency) : '—';
  demo.el('balance-note').textContent = !balanceQueried ? 'Выберите дату и нажмите «Показать».' : waiting ? 'Загружаем баланс…' : error ? 'Не удалось получить баланс. Повторите попытку.' : state === 'empty' ? 'Демонстрационные данные не загружены.' : before ? 'До начала учёта баланс равен нулю.' : !match ? 'Для этой даты нет данных в автономном макете.' : '';
  demo.el('balance-retry').hidden = !error;
}
function renderHistory() {
  const wallet = demo.wallets[historyWallet];
  demo.el('history-pick').hidden = Boolean(wallet); demo.el('history-content').hidden = !wallet;
  demo.el('accounting-note').textContent = wallet ? `${demo.name(historyWallet)} · Учёт с ${demo.date(wallet.start)}. До этой даты баланс равен нулю.` : '';
  if (!wallet) return;
  renderBalance();
  const state = historyRequestError ? 'error' : demo.state();
  const rows = !historyQueried || ['empty','loading','error'].includes(state) ? [] : historyFixtures().filter(row => row.date >= historyRange.from && row.date <= historyRange.to);
  const shown = rows.slice(0,historyLimit);
  demo.el('history-rows').replaceChildren();
  shown.forEach(row => demo.row('history',[demo.date(row.date),{ text: demo.money(row.amount,wallet.currency),className:'amount' },'—']));
  demo.listState('history',state,shown.length > 0,historyQueried ? 'За выбранный период данных нет' : 'Укажите период',historyQueried ? 'Выберите другой диапазон. В макете доступны записи 09–15.09.2026.' : 'Заполните обе даты и нажмите «Показать историю».');
  demo.el('history-period').textContent = historyQueried ? `Период результата: ${demo.date(historyRange.from)} — ${demo.date(historyRange.to)}` : '';
  demo.el('history-count').textContent = ['loading','error'].includes(state) ? '—' : `Показано: ${shown.length}`;
  demo.more('history',shown.length,rows.length);
}
function changeHistoryWallet() {
  historyWallet = demo.el('history-wallet').value; historyQueried = false; balanceQueried = false;
  historyRequestError = false; balanceRequestError = false; historyLimit = 5;
  demo.el('balance-date').value = ''; demo.el('history-from').value = ''; demo.el('history-to').value = '';
  specimen.message('history-range-error',''); ['history-from','history-to'].forEach(id=>demo.el(id).removeAttribute('aria-invalid'));
  renderHistory();
}
demo.el('history-wallet').addEventListener('change',changeHistoryWallet);
demo.el('balance-query-form').addEventListener('submit',event=> {
  event.preventDefault(); balanceDate = demo.el('balance-date').value || demo.today; balanceQueried = true; balanceRequestError = demo.fails();
  if (demo.state() === 'loading') return;
  renderBalance(); demo.el('balance-result').focus();
});
demo.el('history-range').addEventListener('submit',event=> {
  event.preventDefault(); if (demo.state() === 'loading') return;
  const from = demo.el('history-from'), to = demo.el('history-to');
  const error = !from.value || !to.value ? 'Укажите обе даты диапазона.' : from.value > to.value ? 'Начало периода не может быть позже его окончания.' : '';
  specimen.error(from,'history-range-error',error); to.setAttribute('aria-invalid',String(Boolean(error)));
  if (error) { (!from.value || from.value > to.value ? from : to).focus(); return; }
  historyRange = { from: from.value, to: to.value }; historyLimit = 5; historyQueried = true; historyRequestError = demo.fails(); renderHistory();
});
demo.el('balance-retry').addEventListener('click',()=> { demo.el('demo-state').value = 'default'; balanceRequestError = false; renderBalance(); demo.el('balance-result').focus(); });
demo.el('history-retry').addEventListener('click',()=> { demo.el('demo-state').value = 'default'; historyRequestError = false; renderHistory(); demo.el('history-from').focus(); });
demo.el('history-more').addEventListener('click',()=> { historyLimit += 5; renderHistory(); demo.el('history-table').focus(); });
demo.el('demo-state').addEventListener('change',()=> {
  historyRequestError = false; balanceRequestError = false;
  if (demo.state() === 'unselected') { demo.el('history-wallet').value = ''; changeHistoryWallet(); return; }
  if (!historyWallet) { historyWallet = 'daily'; demo.el('history-wallet').value = historyWallet; }
  balanceQueried = true; historyQueried = true; historyLimit = 5;
  historyRange = { from:'2026-09-09',to:'2026-09-15' }; balanceDate = demo.state() === 'before-start' ? '2026-08-31' : demo.today;
  demo.el('history-from').value = historyRange.from; demo.el('history-to').value = historyRange.to; demo.el('balance-date').value = balanceDate;
  specimen.message('history-range-error',''); ['history-from','history-to'].forEach(id=>demo.el(id).removeAttribute('aria-invalid'));
  renderHistory();
});
renderHistory();
