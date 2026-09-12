# ADR-0013: Топология деплоя — Railway (backend) + Vercel (frontend), два окружения

## Статус

Принято — 2026-09-12.

## Контекст

Пользователь хочет: push в GitHub → backend разворачивается на Railway → frontend разворачивается на Vercel → доступ по внешней ссылке. Доступные площадки: GitHub, Supabase, Vercel, Railway. Решение по выбору площадок принято ранее (см. обсуждение в чате, не отдельным ADR): backend + Postgres — оба на Railway (единый provider, минимальная сетевая задержка), frontend — на Vercel; Supabase не используется.

Дополнительное требование пользователя: два окружения с раздельными ветками GitHub —
- `master` → production (сейчас `master` — ранний скелет проекта, до структурного рефакторинга и большинства модулей; пользователь осознанно оставляет его как есть и продвинет вперёд отдельным решением, когда сочтёт нужным).
- `release-candidates/v0.1.0` → development/staging (сейчас содержит весь текущий функционал и структурный рефакторинг ADR-0012).

Пользователь явно выбрал прямые CORS-запросы фронтенда к backend (а не прокси через Vercel rewrites) — то есть frontend на Vercel обращается напрямую к полному URL backend на Railway, и это требует настроенного CORS на backend под конкретные origin.

## Обнаруженные пробелы (на момент структурного рефакторинга backend/frontend уже не имел деплой-конфигурации)

- CORS был закреплён только за `Development` (`localhost:4200`) — вне Development отсутствовал вовсе, прямые кросс-origin запросы с Vercel были бы заблокированы браузером.
- `frontend/src/environments/environment.ts` использовал относительный `apiBaseUrl: '/api/v1'` — не имеет смысла при раздельных origin (frontend и backend на разных доменах).
- Не было `Dockerfile` для backend — Railway для .NET предпочитает Docker Nixpacks-автоопределению.
- Отсутствовал health-check эндпоинт.
- `UseHttpsRedirection()` был безусловным — за обратным прокси Railway это либо no-op (для публичного трафика, т.к. Railway задаёт `X-Forwarded-Proto`), либо ломает health-check самого Railway (опрашивает контейнер напрямую по HTTP внутри приватной сети, без `X-Forwarded-*`).

## Решение

### Backend (Railway)

- `backend/Dockerfile` — multi-stage build (`dotnet/sdk:8.0` → `dotnet/aspnet:8.0`), публикует `src/Host/LupexWallet.Api/LupexWallet.Api.csproj` напрямую (не `.sln` — тестовые проекты не нужны в образе и не копируются, см. `backend/.dockerignore`). `global.json` тоже исключён из контекста — версия SDK внутри образа управляется тегом `FROM`, не локальным dev-пином (несовпадение `8.0.423` из `global.json` и SDK в базовом образе `8.0.416` иначе ломает `rollForward: latestFeature`, который не откатывается назад).
- Слушает `ASPNETCORE_URLS=http://+:$PORT` — `$PORT` задаёт Railway динамически при старте контейнера (shell-form `ENTRYPOINT`, не статичный `ENV`).
- CORS (`Program.cs`) — список разрешённых origin читается из конфигурации `Cors:AllowedOrigins` (env-переменные `Cors__AllowedOrigins__0`, `__1`, ...), применяется во всех окружениях; `http://localhost:4200` добавляется автоматически только в Development.
- `app.UseForwardedHeaders(...)` (X-Forwarded-For/Proto) добавлен перед остальным пайплайном — без него `UseHttpsRedirection` не видит, что публичный запрос уже пришёл по HTTPS через Railway.
- `/health` — анонимный эндпоинт, явно исключён из HTTPS-редиректа (`UseWhen`), т.к. Railway health-check обращается к контейнеру напрямую по HTTP в приватной сети, минуя edge-прокси.
- EF Core-миграции всех 6 схем применяются автоматически при старте (уже было реализовано ранее, до этого ADR) — отдельного шага миграции в CI/CD не требуется.
- Обязательные production-переменные окружения (без них приложение не стартует вне Development — проверка была в коде раньше): `ConnectionStrings__LupexWallet`, `Auth__PasswordHash`, `Auth__JwtSigningKey`.

**Два Railway-окружения** в одном проекте: `production` (branch `master`, root directory `backend/`) и `staging`/`development` (branch `release-candidates/v0.1.0`, тот же root directory). У каждого — свой Postgres-плагин (Railway создаёт его на окружение) и свой набор переменных (включая свой `Cors__AllowedOrigins`, указывающий на соответствующий Vercel-домен).

### Frontend (Vercel)

- `frontend/scripts/write-env.mjs` перезаписывает `src/environments/environment.ts` перед сборкой, читая `API_BASE_URL` из переменных окружения сборки — так один и тот же билд-процесс работает для обоих Vercel-окружений с разным backend URL. Без переменной — откатывается на прежний относительный `/api/v1` (не ломает локальную сборку `ng build` вне Vercel).
- `package.json`: скрипт `build:deploy` = `write-env.mjs && ng build`.
- `frontend/vercel.json` — `buildCommand: npm run build:deploy`, `outputDirectory: dist/lupex-wallet-web/browser`, SPA-rewrite (`/(.*) → /index.html`, стандартный паттерн Vercel для client-side роутинга Angular).

**Два Vercel-окружения**: Production Branch = `master`; `release-candidates/v0.1.0` получает обычный Vercel Preview-деплой со стабильным branch-alias URL. `API_BASE_URL` задаётся раздельно для Production и Preview в настройках проекта Vercel (каждое — на соответствующий Railway-URL).

### Циклическая зависимость URL

Frontend должен знать URL backend (`API_BASE_URL`), backend должен знать URL frontend (`Cors__AllowedOrigins`) — оба известны только после первого деплоя на соответствующей площадке. Разрывается порядком: сначала Railway (получить URL backend) → задать `API_BASE_URL` в Vercel и задеплоить frontend (получить URL/alias) → вернуться в Railway и задать `Cors__AllowedOrigins`.

## Последствия

- Прямые CORS-запросы (а не прокси через Vercel) — минус: при смене домена Vercel нужно вручную обновить `Cors__AllowedOrigins` в Railway. Плюс: проще отлаживать (fetch идёт на реальный backend-домен, а не через прокси), меньше движущихся частей на стороне Vercel.
- Два независимых Railway-окружения — два отдельных Postgres, данные prod и staging не пересекаются (осознанный выбор, не обсуждался отдельно, но следует из «два окружения = двух независимые ветки»).
- `master` временно не деплоится по факту (Dockerfile и текущая раскладка `backend/` там отсутствуют, т.к. `master` не обновлён) — production Railway/Vercel окружения можно создать уже сейчас (настройки не изменятся), но первый успешный деплой prod возможен только после того, как пользователь продвинет `master` вперёд.

## Связанные материалы

- [ADR-0012: Структура репозитория и конвенции](0012-repository-structure-and-conventions.md)
- [docs/PROGRESS.md](../../PROGRESS.md)
