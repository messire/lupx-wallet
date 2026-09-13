# Конвенция: backend

Короткая справка по размещению файлов внутри `backend/src/Modules/<Module>/`. Полный шаблон и обоснование — [ADR-0012 §2](../architecture/adr/0012-repository-structure-and-conventions.md#2-единый-шаблон-backend-модуля), границы модулей — [ADR-0006](../architecture/adr/0006-modular-monolith-boundaries.md), [high-level-architecture.md](../architecture/high-level-architecture.md).

## Куда положить файл

| Назначение | Расположение |
|---|---|
| Пользовательский сценарий (команда/запрос + обработчик) | `Application/UseCases/<Action>/` |
| Правила и инварианты сущности, события, исключения домена | `Domain/`, `Domain/Events/`, `Domain/Exceptions/` |
| Интерфейс хранения (репозиторий, unit of work) | `Application/Abstractions/Persistence/` |
| Интерфейс межмодульного взаимодействия (порт, который реализует другой модуль или который потребляют другие модули) | `Application/Abstractions/Integration/` |
| Общие для сценариев модели/DTO и политики | `Application/Models/`, `Application/Policies/` |
| EF Core DbContext, конфигурации, репозитории, миграции | `Infrastructure/Persistence/{Configurations,Repositories,Migrations}/` |
| Межмодульные адаптеры и обработчики доменных событий других модулей | `Infrastructure/Integration/` |
| Фоновые задачи (`BackgroundService` и т.п.) | `Infrastructure/BackgroundJobs/` |
| Внешние HTTP-клиенты (например Frankfurter) | `Infrastructure/ExternalServices/` |
| HTTP-контракты и маппинг эндпоинтов | `Api/Contracts/`, `Api/Endpoints/` |

## От чего может зависеть слой

`Domain` не зависит ни от чего (кроме `SharedKernel`). `Application` зависит только от своего `Domain` (+ `Application` других модулей — по факту cross-module ссылки). `Infrastructure` реализует интерфейсы `Application`, может ссылаться на `Domain` других модулей (для обработчиков доменных событий — не на их `Application`/`Infrastructure`). `Api` зависит от `Application`, не от `Infrastructure` напрямую (только через DI).

**Правило границы модуля** (ADR-0006/0007/0009): `Domain`/`Application` модуля X не ссылается на `Domain`/`Infrastructure` модуля Y напрямую. Связь — через типизированный Id (`SharedKernel`), доменное событие, или явный consumer-owned порт в `Application/Abstractions/Integration/`.

## Именование и тесты

- PascalCase для папок и файлов типов. Полные имена `.csproj`/сборок (`LupexWallet.<Module>.<Layer>.csproj`) не сокращать — сокращаются только имена папок слоёв.
- У `Reporting` нет `Domain` (нет агрегатов) — не создавать ради симметрии.
- Тесты — в `backend/tests/LupexWallet.UnitTests/<Модуль>/` (домен/application, без внешних зависимостей) и `backend/tests/LupexWallet.IntegrationTests/<Модуль>/` (реальный Postgres через Testcontainers, реальный Host), по существующей группировке.
- Doc-комментарии — см. [repository-structure.md](repository-structure.md).
