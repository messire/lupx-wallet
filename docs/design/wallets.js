/* Local specimen interactions; no requests, storage or financial mutations. */
const tabs = [...document.querySelectorAll('[role="tab"]')];
const grid = document.querySelector('#wallet-grid');
const statePanel = document.querySelector('#wallet-state');
const stateTitle = document.querySelector('#state-title');
const stateDescription = document.querySelector('#state-description');
const total = document.querySelector('#total-balance');
const balanceNote = document.querySelector('#balance-note');
const sampleState = document.querySelector('#sample-state');
const retry = document.querySelector('#retry');
const firstName = grid.querySelector('h3');
const firstBalance = grid.querySelector('.balance');
const originalName = firstName.textContent;
const originalBalance = firstBalance.textContent;
const sidebar = document.querySelector('.sidebar');
const menuToggle = document.querySelector('.menu-toggle');
const announce = text => { document.querySelector('#announcement').textContent = text; };

function renderState() {
  const archived = tabs[1].getAttribute('aria-selected') === 'true';
  const state = sampleState.value;
  const unavailable = state === 'error' || state === 'loading';
  grid.hidden = archived || ['empty', 'loading', 'error'].includes(state);
  statePanel.hidden = !grid.hidden;
  statePanel.classList.toggle('loading', state === 'loading');
  document.querySelector('#wallet-panel').setAttribute('aria-busy', String(state === 'loading'));
  retry.hidden = state !== 'error';
  total.textContent = unavailable ? '—' : state === 'empty' ? '0,00 €' : state === 'long' ? '1 234 567 900,12 €' : '12 480,50 €';
  balanceNote.textContent = unavailable ? 'Баланс пока недоступен' : state === 'empty' ? 'Нет кошельков' : '3 кошелька · Все суммы в EUR';
  firstName.textContent = state === 'long' ? 'Ежедневные расходы семьи и покупки для дома' : originalName;
  firstBalance.textContent = state === 'long' ? '1 234 557 900,12 €' : originalBalance;
  const descriptions = {
    empty: ['Кошельков пока нет', 'Создайте первый кошелёк, чтобы начать учёт.'],
    loading: ['Загружаем кошельки', 'Подождите немного.'],
    error: ['Не удалось загрузить кошельки', 'Повторите попытку.'],
    archive: ['Архивных кошельков пока нет', 'Здесь появятся кошельки, перемещённые в архив.']
  };
  const copy = descriptions[state] || (archived ? descriptions.archive : ['', '']);
  stateTitle.textContent = copy[0];
  stateDescription.textContent = copy[1];
  document.querySelector('#recent').hidden = ['empty', 'loading', 'error', 'long'].includes(state);
  announce(copy[0] || 'Показаны активные кошельки');
}

function selectTab(tab) {
  tabs.forEach(item => {
    item.setAttribute('aria-selected', String(item === tab));
    item.tabIndex = item === tab ? 0 : -1;
  });
  document.querySelector('#wallet-panel').setAttribute('aria-labelledby', tab.id);
  renderState();
}

tabs.forEach((tab, index) => {
  tab.addEventListener('click', () => selectTab(tab));
  tab.addEventListener('keydown', event => {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : (index + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
    selectTab(tabs[next]);
    tabs[next].focus();
  });
});
sampleState.addEventListener('change', renderState);
retry.addEventListener('click', () => { sampleState.value = 'default'; selectTab(tabs[0]); tabs[0].focus(); });

function closeMenu(restoreFocus = false) {
  sidebar.classList.remove('is-open');
  menuToggle.setAttribute('aria-expanded', 'false');
  if (restoreFocus) menuToggle.focus();
}
menuToggle.addEventListener('click', () => {
  const open = sidebar.classList.toggle('is-open');
  menuToggle.setAttribute('aria-expanded', String(open));
  if (open) sidebar.querySelector('.current').focus();
});
sidebar.querySelector('.current').addEventListener('click', () => closeMenu());
document.addEventListener('keydown', event => {
  if (event.key !== 'Escape') return;
  if (sidebar.classList.contains('is-open')) closeMenu(true);
  document.querySelectorAll('.wallet-menu[open]').forEach(menu => { menu.open = false; menu.querySelector('summary').focus(); });
});
window.matchMedia('(min-width:1100px)').addEventListener('change', () => closeMenu());

const screenNote = document.querySelector('#screen-note');
document.querySelectorAll('[data-preview]').forEach(button => button.addEventListener('click', () => {
  document.querySelector('#screen-note-title').textContent = button.dataset.preview;
  screenNote.showModal();
}));
renderState();
