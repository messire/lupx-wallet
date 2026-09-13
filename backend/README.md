# LupexWallet — backend

ASP.NET Core / EF Core / PostgreSQL, модульный монолит. Структура модуля и конвенции размещения — см. [ADR-0012](../docs/architecture/adr/0012-repository-structure-and-conventions.md) и [docs/architecture/high-level-architecture.md](../docs/architecture/high-level-architecture.md).

## Структура

- `src/BuildingBlocks/` — `SharedKernel` (без внешних зависимостей), `Infrastructure` (сквозная инфраструктура)
- `src/Modules/<Module>/` — по бизнес-модулю: `Domain`, `Application`, `Infrastructure`, `Api`
- `src/Host/LupexWallet.Api/` — точка входа, композиция модулей
- `tests/LupexWallet.UnitTests/`, `tests/LupexWallet.IntegrationTests/`

## Запуск

Требуется .NET SDK 8.0.x (версия зафиксирована в [global.json](global.json)).

```bash
dotnet build
dotnet run --project src/Host/LupexWallet.Api
```

По умолчанию слушает `http://localhost:5199` (см. `launchSettings.json`/переданный `--urls`), Swagger UI доступен в Development-режиме на `/swagger`.

### Аутентификация (локальная разработка)

В `appsettings.Development.json` уже задан пароль для локальной разработки:

```
Пароль: ChangeMe123!
```

Не использовать в production. Для production `Auth:PasswordHash` и `Auth:JwtSigningKey` обязательны и задаются через переменные окружения (`Auth__PasswordHash`, `Auth__JwtSigningKey`) или secret manager — при пустых значениях приложение не запустится вне Development (см. `src/Host/LupexWallet.Api/Program.cs`). Хеш пароля генерируется через `LupexWallet.Api.Auth.PasswordHasher.Hash(...)` (PBKDF2-HMACSHA256).

## PostgreSQL для локальной разработки

Из корня репозитория:

```bash
docker compose up -d
```

Требует заранее созданную внешнюю docker-сеть `docker-network-shared` (`docker network create docker-network-shared`, если её ещё нет).

Поднимает `postgres` на `localhost:5432` (БД `postgres`, пользователь/пароль `postgres-user`/`postgres-pwd` — совпадает со строкой подключения в `appsettings.Development.json`), данные сохраняются в именованном томе между перезапусками. Дополнительно поднимается `pgadmin4` на `http://localhost:5050` (вход `pgadmin4@pgadmin.org` / `admin`) для просмотра БД через UI.

## Backend в Docker (без запуска из IDE)

Если backend не нужно отлаживать в IDE (например, отлаживается только frontend), его можно
поднять в Docker вместе с postgres — см. `docker-compose.backend.yml` в корне репозитория и
раздел [«Независимый запуск backend/frontend в Docker»](../README.md#независимый-запуск-backendfrontend-в-docker)
в корневом README:

```bash
docker compose -f docker-compose.yml -f docker-compose.backend.yml up -d --build
```

Слушает тот же `localhost:5086`, что и при запуске из IDE.

## Тесты

```bash
dotnet test tests/LupexWallet.UnitTests/LupexWallet.UnitTests.csproj        # unit — домен/application, без внешних зависимостей
dotnet test tests/LupexWallet.IntegrationTests/LupexWallet.IntegrationTests.csproj  # integration — реальный Postgres через Testcontainers (нужен запущенный Docker), реальный Host через WebApplicationFactory
```

Интеграционные тесты поднимают собственный одноразовый контейнер Postgres (не зависят от `docker compose up`), сами применяют все EF Core-миграции и сбрасывают данные между тестами через Respawn — `docker compose`-инстанс им не нужен.

Оба набора тестов и архитектурная проверка запускаются в CI как отдельные job'ы — [`.github/workflows/ci.yml`](../.github/workflows/ci.yml).

## Архитектурная проверка

```bash
node scripts/check-architecture.mjs
```

Проверяет граф `ProjectReference`/`PackageReference` в `backend/src/**/*.csproj` против правила границы модуля (`high-level-architecture.md` §2, ADR-0006/0007/0009): какой слой какому может ссылаться, что Domain не подключает NuGet-пакеты, что Application не подключает EF Core/ASP.NET Core напрямую, и что граф ссылок между проектами не содержит циклов. Не требует сборки решения — читает `.csproj` напрямую, поэтому ловит нарушение раньше и быстрее, чем `dotnet build` (успешная компиляция сама по себе не гарантирует соблюдение правила границы модуля). Область — только `backend/src`; `backend/tests` намеренно не проверяется (интеграционные тесты закономерно ссылаются на несколько модулей и Host). Правила проверки покрыты примерами разрешённых/запрещённых зависимостей — `scripts/lib/module-rules.test.mjs` (`node --test scripts/lib/module-rules.test.mjs`).

## Деплой (Railway)

Топология и обоснование — [ADR-0013](../docs/architecture/adr/0013-deployment-topology.md). `Dockerfile` в корне `backend/` — Root Directory сервиса в Railway должен быть `backend/`.

Обязательные переменные окружения (приложение не стартует без них вне Development):

| Переменная | Назначение |
|---|---|
| `ConnectionStrings__LupexWallet` | строка подключения к Postgres (Railway подставляет автоматически при линковке Postgres-плагина через `${{Postgres.DATABASE_URL}}`-подобные ссылки — сверить формат под Npgsql: `Host=...;Port=...;Database=...;Username=...;Password=...`) |
| `Auth__PasswordHash` | хеш пароля для входа (генерируется `LupexWallet.Api.Auth.PasswordHasher.Hash(...)`, не тот же, что в dev) |
| `Auth__JwtSigningKey` | ключ подписи JWT, случайная строка ≥32 байт |
| `Cors__AllowedOrigins__0` (и `__1`, `__2`, ...) | origin'ы фронтенда, которым разрешены кросс-origin запросы (например `https://lupex-wallet.vercel.app`) |
| `PORT` | задаётся Railway автоматически, вручную не указывать |

EF Core-миграции всех модулей применяются автоматически при старте — отдельный шаг для CI/CD не нужен. `/health` — анонимный эндпоинт для health-check Railway.
