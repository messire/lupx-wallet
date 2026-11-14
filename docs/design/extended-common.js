/* Shared primitives for the documentation specimens, not application services. */
const demo = {
  today: '2026-09-15',
  wallets: {
    daily: { name: 'Ежедневные расходы', currency: 'EUR', start: '2026-09-01' },
    savings: { name: 'Накопления', currency: 'EUR', start: '2026-09-01' },
    travel: { name: 'Путешествия', currency: 'EUR', start: '2026-09-05' },
    usd: { name: 'Долларовый запас', currency: 'USD', start: '2026-09-01' }
  },
  el(id) { return document.getElementById(id); },
  date(value) { return value.split('-').reverse().join('.'); },
  money(value, currency) { return specimen.formatAmount(value) + ' ' + currency; },
  state() { return this.el('demo-state').value; },
  fails() { return this.el('demo-result').value === 'error'; },
  name(key) { return this.state() === 'long' && key === 'daily' ? 'Ежедневные расходы семьи и покупки для дома' : this.wallets[key].name; },
  row(id, cells) {
    const row = document.createElement('tr');
    cells.forEach(cell => {
      const td = document.createElement('td');
      if (typeof cell === 'string') td.textContent = cell;
      else { td.className = cell.className || ''; if (cell.node) td.append(cell.node); else td.textContent = cell.text; }
      row.append(td);
    });
    this.el(id + '-rows').append(row);
  },
  listState(id, state, hasRows, emptyTitle, emptyCopy) {
    const unavailable = state === 'loading' || state === 'error';
    this.el(id + '-table').hidden = unavailable || !hasRows;
    this.el(id + '-state').hidden = !unavailable && hasRows;
    this.el(id + '-list').setAttribute('aria-busy', String(state === 'loading'));
    this.el(id + '-state').classList.toggle('loading', state === 'loading');
    this.el(id + '-state-title').textContent = state === 'loading' ? 'Загружаем данные…' : state === 'error' ? 'Не удалось загрузить данные' : emptyTitle;
    this.el(id + '-state-copy').textContent = state === 'error' ? 'Повторите попытку. Выбранные условия сохранены.' : state === 'loading' ? 'Подождите немного.' : emptyCopy;
    this.el(id + '-retry').hidden = state !== 'error';
  },
  more(id, shown, total) { this.el(id + '-more').hidden = !shown || shown >= total; },
  clearErrors(form) { form.querySelectorAll('.field-error').forEach(e => { e.hidden = true; }); form.querySelectorAll('[aria-invalid]').forEach(e => e.removeAttribute('aria-invalid')); }
};
