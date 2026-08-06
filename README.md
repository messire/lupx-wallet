# LupexWallet

Однопользовательское приложение для учета личных финансов. Стек: Angular (frontend), ASP.NET Core / EF Core / PostgreSQL (backend, модульный монолит). Полное описание требований и архитектуры — в [docs/](docs/).

## Структура репозитория

- [docs/requirements/](docs/requirements/) — бизнес-требования, пользовательские сценарии, глоссарий
- [docs/architecture/](docs/architecture/) — DDD-модель, высокоуровневая архитектура, ADR
- [docs/database/](docs/database/) — схема PostgreSQL
- [docs/api/](docs/api/) — OpenAPI-контракт
- `src/` — backend (.NET), модульный монолит: `BuildingBlocks/`, `Modules/<Module>/{Domain,Application,Infrastructure,Api}`, `Host/LupexWallet.Api`
- `frontend/` — Angular SPA

## Запуск backend

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

Не использовать в production. Для production `Auth:PasswordHash` и `Auth:JwtSigningKey` обязательны и задаются через переменные окружения (`Auth__PasswordHash`, `Auth__JwtSigningKey`) или secret manager — при пустых значениях приложение не запустится вне Development (см. `Program.cs`). Хеш пароля генерируется через `LupexWallet.Api.Auth.PasswordHasher.Hash(...)` (PBKDF2-HMACSHA256).

## Запуск frontend

Требуется Node.js 20+.

```bash
cd frontend
npm install
npm start
```

По умолчанию открывается на `http://localhost:4200` и обращается к API на `http://localhost:5199` (см. `src/environments/environment.development.ts`). CORS для `localhost:4200` включен в backend только в Development-режиме.

## PostgreSQL для локальной разработки

```bash
docker compose up -d
```

Требует заранее созданную внешнюю docker-сеть `docker-network-shared` (`docker network create docker-network-shared`, если её ещё нет).

Поднимает `postgres` на `localhost:5432` (БД `postgres`, пользователь/пароль `postgres-user`/`postgres-pwd` — совпадает со строкой подключения в `appsettings.Development.json`), данные сохраняются в именованном томе между перезапусками. Дополнительно поднимается `pgadmin4` на `http://localhost:5050` (вход `pgadmin4@pgadmin.org` / `admin`) для просмотра БД через UI.

## Тесты

Backend (`tests/`):

```bash
dotnet test tests/LupexWallet.UnitTests/LupexWallet.UnitTests.csproj        # unit — домен/application, без внешних зависимостей
dotnet test tests/LupexWallet.IntegrationTests/LupexWallet.IntegrationTests.csproj  # integration — реальный Postgres через Testcontainers (нужен запущенный Docker), реальный Host через WebApplicationFactory
```

Интеграционные тесты поднимают собственный одноразовый контейнер Postgres (не зависят от `docker compose up`), сами применяют все EF Core-миграции и сбрасывают данные между тестами через Respawn — `docker compose`-инстанс им не нужен.

Frontend:

```bash
cd frontend
npm test
```

## Текущий статус реализации

Актуальный статус по срезам, известные упрощения и инфраструктурный TODO — в [docs/PROGRESS.md](docs/PROGRESS.md) (единственный источник правды по прогрессу, этот README не дублирует таблицу).
