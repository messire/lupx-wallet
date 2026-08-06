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

## Текущий статус реализации

Реализован первый вертикальный срез — аутентификация по единому паролю (`POST /api/v1/auth/login`), end-to-end: backend (JWT, PBKDF2, rate limiting на попытки входа) + frontend (форма входа, guard, интерцептор, хранение токена). Остальные модули (Wallets, Operations, BalanceHistory, ExchangeRates, ReferenceData, Audit, Reporting) присутствуют в решении как скелет (собираются, зарегистрированы в композиции), но без доменной логики — она добавляется последующими вертикальными срезами.
