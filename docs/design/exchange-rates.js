/* Illustrative quotations only. No exchange-rate service is called. */
const rateRows = [ ['USD','EUR','0.8503401361'], ['GBP','EUR','1.1764705882'], ['CHF','EUR','1.0638297872'], ['JPY','EUR','0.0060606061'] ];
let ratesHaveData = true, ratesStamp = '15.09.2026, 10:30', ratesBusy = false, ratesTimer;
function renderRates() {
  const state = demo.state();
  const visibleRows = ratesHaveData && !['loading','error'].includes(state);
  demo.el('rates-rows').replaceChildren();
  if (visibleRows) rateRows.forEach(([from,to,rate])=>demo.row('rates',[from,to,{text:specimen.formatAmount(state === 'long' && from === 'JPY' ? '0.0060606060606060606060606061' : rate),className:'amount'}]));
  demo.el('rates-updated').textContent = state === 'loading' ? '—' : state === 'error' ? 'Недоступно' : ratesHaveData ? ratesStamp : 'Ещё не было';
  demo.listState('rates',state,visibleRows,'Курсы валют ещё не загружены','Нажмите «Обновить сейчас», чтобы получить курсы.');
  demo.el('refresh-rates').disabled = ratesBusy || state === 'loading';
  demo.el('refresh-rates').textContent = ratesBusy ? 'Обновление…' : 'Обновить сейчас';
  demo.el('rates-list').setAttribute('aria-busy',String(ratesBusy || state === 'loading'));
  specimen.message('rates-warning',state === 'stale' ? 'Источник недоступен. Показаны последние известные курсы; время успешного обновления не изменилось.' : state === 'stale-empty' ? 'Источник недоступен. Сохранённых курсов пока нет — повторите обновление позже.' : '');
}
demo.el('demo-state').addEventListener('change',()=> {
  clearTimeout(ratesTimer); ratesBusy = false;
  ratesHaveData = !['empty','stale-empty'].includes(demo.state()); renderRates();
});
demo.el('refresh-rates').addEventListener('click',()=> {
  if (ratesBusy) return;
  ratesBusy = true; renderRates();
  ratesTimer = setTimeout(()=> {
    ratesBusy = false;
    if (demo.fails()) demo.el('demo-state').value = ratesHaveData ? 'stale' : 'stale-empty';
    else { ratesHaveData = true; ratesStamp = '15.09.2026, 10:31'; demo.el('demo-state').value = 'default'; }
    renderRates(); if (!demo.fails()) specimen.message('rates-warning','Курсы обновлены в макете.');
    demo.el('rates-warning').focus();
  },500);
});
demo.el('rates-retry').addEventListener('click',()=> { demo.el('demo-state').value = 'default'; renderRates(); demo.el('refresh-rates').focus(); });
renderRates();
