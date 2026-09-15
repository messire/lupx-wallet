# ADR-0008: Деплой — Railway для backend, Vercel для frontend, одно окружение

## Статус

Принято — 2026-09-12. Приведено к фактической схеме 2026-09-30.

**Состояние реализации:** развёрнуто и работает (со слов пользователя).

## Контекст

Пользователь хочет: push в GitHub → backend разворачивается на Railway → frontend разворачивается на Vercel → доступ по внешней ссылке. Backend и PostgreSQL — на Railway (один провайдер, минимальная задержка), frontend — на Vercel; Supabase не используется. Frontend обращается к backend напрямую (CORS), а не через прокси Vercel.

Первоначально планировались два окружения (production и staging) с раздельными ветками; эта схема не была развёрнута. Этот ADR описывает то, что развёрнуто и остаётся целевым.

## Решение

### Топология

| Площадка | Окружение | Ветка |
|---|---|---|
| Railway: backend + PostgreSQL | одно | `master` |
| Vercel: Production | одно | `master` |
| Vercel: Preview | на каждую остальную ветку | обращается к тому же единственному backend |

Отдельного staging нет. Один backend, одна база.

### Backend (Railway)

- `backend/Dockerfile` — multi-stage (`dotnet/sdk:8.0` → `dotnet/aspnet:8.0`), публикует `src/Host/LupexWallet.Api` напрямую, без `.sln` и тестовых проектов (`backend/.dockerignore`). `global.json` исключён из контекста: версия SDK в образе задаётся тегом `FROM`, локальный пин с `rollForward: latestFeature` иначе ломает сборку при более старом SDK в образе.
- Слушает `ASPNETCORE_URLS=http://+:$PORT`; `$PORT` задаёт Railway при старте (shell-form `ENTRYPOINT`).
- CORS: список разрешённых origin читается из `Cors:AllowedOrigins` (`Cors__AllowedOrigins__0`, …) во всех окружениях; `http://localhost:4200` добавляется только в Development.
- `UseForwardedHeaders` (`X-Forwarded-For`/`X-Forwarded-Proto`) — перед остальным пайплайном, иначе `UseHttpsRedirection` за прокси Railway не видит, что запрос уже по HTTPS.
- `/health` — анонимный эндпоинт, исключён из HTTPS-редиректа: Railway опрашивает контейнер по HTTP внутри приватной сети.
- Миграции всех схем применяются при старте — отдельного шага в CI/CD нет.
- Обязательные переменные вне Development: `ConnectionStrings__LupexWallet`, `Auth__PasswordHash`, `Auth__JwtSigningKey`; без них приложение не стартует.

### Frontend (Vercel)

- `frontend/scripts/write-env.mjs` перезаписывает `src/environments/environment.ts` перед сборкой, читая `API_BASE_URL` из переменных сборки; без переменной — относительный `/api/v1`, локальная сборка не ломается.
- `package.json`: `build:deploy` = `write-env.mjs && ng build`.
- `frontend/vercel.json`: `buildCommand: npm run build:deploy`, `outputDirectory: dist/lupex-wallet-web/browser`, SPA-rewrite на `index.html`.
- `API_BASE_URL` задаётся в настройках проекта Vercel и для Production, и для Preview — на один и тот же backend.

### Циклическая зависимость адресов

Frontend должен знать адрес backend, backend — адрес frontend. Разрывается порядком: сначала Railway (адрес backend) → `API_BASE_URL` в Vercel и деплой frontend (адрес) → `Cors__AllowedOrigins` в Railway.

## Последствия

- Прямые CORS-запросы: при смене домена Vercel нужно вручную обновить `Cors__AllowedOrigins`; зато проще отлаживать и меньше движущихся частей.
- **Preview-сборка любой ветки читает и пишет боевые данные.** После [ADR-0002](./0002-record-lifecycle.md) записи с операциями нельзя стереть окончательно. Риск назван пользователю и принят.
- Preview ветки, меняющей API, несовместим с backend из `master` до слияния.
- Один экземпляр backend соответствует ограничению [ADR-0001](./0001-modular-monolith.md), §8.
- Неизвестное: preview-сборки, по словам пользователя, работают со входом, но каким образом их адреса разрешены в CORS, в репозитории не видно — код допускает только точные адреса из `Cors:AllowedOrigins`, значения заданы в настройках Railway.

## Отклонённые варианты

- **Два окружения Railway с привязкой staging к ветке версии** — не развёрнуто; требовало бы перенастройки площадок при каждой новой ветке версии и второй базы.
- **Preview без backend** — предложено как защита боевых данных; пользователь предпочёл рабочие preview.
- **Постоянная ветка staging** — пользователь оставил одну схему `master` → production.
- **Прокси через Vercel rewrites** — пользователь выбрал прямые запросы.

## Связанные материалы

- [ADR-0007](./0007-repository-structure.md) — расположение `Dockerfile`, `vercel.json`, скриптов.
- `backend/README.md`, `frontend/README.md` — команды запуска.
- [docs/PROGRESS.md](../../PROGRESS.md), «Решения пользователя по итогам анализа ADR», вопрос 6.
