# Высокоуровневая архитектура

Источники: [ddd-model.md](./ddd-model.md), ADR-0001…ADR-0006, раздел 9 бизнес-требований (стек: Angular, ASP.NET Core, EF Core, PostgreSQL, модульный монолит).

Технические идентификаторы (имена проектов, модулей, namespace) — на английском. Название решения условно взято от имени репозитория: `LupexWallet` — при необходимости легко переименовать, это не бизнес-решение.

## 1. Общий вид

```
┌─────────────────────────────────────────────────────────────┐
│  Angular SPA (frontend/)                                     │
└───────────────────────────────┬───────────────────────────────┘
                                 │ HTTPS / REST (JSON)
┌───────────────────────────────▼───────────────────────────────┐
│  LupexWallet.Api  — единственный ASP.NET Core host             │
│  (композиция эндпоинтов всех модулей, единый Swagger/OpenAPI)  │
├──────────┬──────────┬───────────┬──────────────┬───────────────┤
│ Wallets  │Reference │Operations │BalanceHistory │ ExchangeRates │
│ module   │Data      │module     │module         │ module        │
├──────────┴──────────┴───────────┴──────────────┴───────────────┤
│ Reporting module (read-only, композиция поверх остальных)      │
├──────────────────────────────────────────────────────────────┤
│ Audit module (сквозной, подписан на события всех модулей)      │
├──────────────────────────────────────────────────────────────┤
│ SharedKernel (Money, типизированные Id, IDomainEvent, база)    │
└───────────────────────────────┬───────────────────────────────┘
                                 │ Npgsql (один физический коннекшн на транзакцию)
┌───────────────────────────────▼───────────────────────────────┐
│  PostgreSQL — одна база, отдельная схема на модуль             │
│  wallets | reference_data | operations | balance_history |     │
│  exchange_rates | audit                                        │
└─────────────────────────────────────────────────────────────┘
```

Один физический деплой (один процесс ASP.NET Core, одна база PostgreSQL) — модульность обеспечивается на уровне кода и схем БД, а не сетевых границ. Это прямо соответствует разделу 9 требований («общий подход: модульный монолит») и осознанно откладывает переход к микросервисам (если он вообще понадобится для однопользовательского приложения).

## 2. Структура решения (.NET)

```
backend/src/
  BuildingBlocks/
    LupexWallet.SharedKernel            — Money, CurrencyId и др. общие VO, IAggregateRoot, IDomainEvent, базовые типизированные Id
    LupexWallet.BuildingBlocks.Infrastructure — MediatR pipeline behaviors (транзакция + диспетчер событий), общие абстракции репозиториев

  Modules/
    Wallets/
      LupexWallet.Wallets.Domain        — Wallet, PrimaryWalletPolicy, инварианты, события
      LupexWallet.Wallets.Application   — команды/запросы (CQRS/MediatR), интерфейсы репозиториев
      LupexWallet.Wallets.Infrastructure— EF Core DbContext (схема wallets), репозитории
      LupexWallet.Wallets.Api           — маппинг HTTP-эндпоинтов, DTO контракты

    ReferenceData/    (WalletType, OperationType, Currency, OperationBehaviorKind) — те же 4 слоя
    Operations/        (Operation, Transfer) — те же 4 слоя
    BalanceHistory/    (BalanceSnapshot, BalanceRecalculationService, фоновая задача снепшота) — те же 4 слоя
    ExchangeRates/      (ExchangeRateQuote, клиент Frankfurter, фоновая задача обновления) — те же 4 слоя
    Reporting/          (Total Amount / Historical Total Amount) — только Application + Api + тонкая Infrastructure для read-запросов через схемы
    Audit/              (AuditEntry, обработчики событий всех модулей) — те же 4 слоя, но Domain предельно простой

  Host/
    LupexWallet.Api      — Program.cs, композиция всех модулей, аутентификация (см. открытый вопрос ниже), Swagger

frontend/
  LupexWallet Angular SPA — отдельное Angular-приложение (раздел 9 требований), общается с LupexWallet.Api только по REST
```

Каждый модуль — набор из 4 проектов (Domain / Application / Infrastructure / Api), кроме `Reporting` (без своего Domain — у него нет агрегатов, см. `ddd-model.md`) и `Audit` (Domain предельно простой — одна сущность `AuditEntry`). Слои внутри модуля — классическая слоистая архитектура (Clean/Onion): `Domain` не зависит ни от чего; `Application` зависит только от `Domain`; `Infrastructure` реализует интерфейсы, объявленные в `Application`; `Api` зависит от `Application`, но не от `Infrastructure` напрямую (только через DI).

**Правило границы модуля:** проект `Modules/X/*.Domain` и `*.Application` другого модуля **не может ссылаться** на `Modules/Y/*.Domain`/`*.Infrastructure` напрямую. Единственные разрешенные способы связи между модулями:
1. Ссылка по строго типизированному Id (например, `Operation.WalletId: WalletId` — тип `WalletId` живет в `SharedKernel`, а не в `Wallets.Domain`, чтобы не тянуть зависимость на весь модуль Wallets).
2. Доменное событие (см. §4).
3. Узкий порт, объявленный в `Application` **владельца данных** и реализованный в его `Infrastructure`; вызывает `Application` потребителя (например, `IWalletBalanceGateway`, `IWalletTotalsSource`; записывающие порты ReferenceData — `IAvailableCurrencyCatalog` для ExchangeRates (замена списка доступных валют, ADR-0004 §7) и `ICurrencyActivation` для Wallets (активация валюты при создании кошелька)). Может быть и записывающим.
4. Узкий порт, объявленный в `Application` **потребителя**, когда прямая ссылка образовала бы цикл; реализации — в `Infrastructure` одного или нескольких владельцев данных, потребитель внедряет `IEnumerable<TПорт>` и агрегирует ответы сам (например, `IWalletHistorySource`, `IReferenceItemUsageProbe`). Пустой список реализаций — ошибка конфигурации, а не отрицательный ответ.
5. `*.Infrastructure`-подписчик на доменное событие другого модуля ссылается на `*.Domain` издателя ради типа события (не бизнес-логики).

Подробно, с критерием выбора между портом и событием — [ADR-0001](./adr/0001-modular-monolith.md), §2–§3.

## 3. Данные: одна база, схема на модуль

- Один физический экземпляр PostgreSQL, шесть схем: `wallets`, `reference_data`, `operations`, `balance_history`, `exchange_rates`, `audit`.
- У каждого модуля (кроме `Reporting`) — собственный `DbContext` и собственные EF Core-миграции, ограниченные своей схемой. Это дает настоящую компиляционную границу: код модуля `Operations` физически не может обратиться к `DbSet<Wallet>`, потому что у него нет такого `DbContext`.
- `Reporting` не имеет своей схемы на запись; для чтения использует либо узкие read-контракты других модулей (см. §2, п.3), либо (как оптимизация в будущем) SQL-представления (views), объединяющие данные из нескольких схем — что допустимо для чисто читающего CQRS-модуля и не нарушает границу записи.

## 3.1 CQRS-маркеры (уточнено при реализации скелета)

`SharedKernel` объявляет пустые маркеры `ICommand<TResponse>` / `IQuery<TResponse>`, без зависимости от MediatR (иначе она транзитивно протекла бы в `Domain` через `SharedKernel`). Конкретная команда/запрос в `Application`-слое модуля реализует одновременно `MediatR.IRequest<TResponse>` (для диспетчеризации) и один из этих маркеров — так `TransactionBehavior` (§4) может ограничиться только командами через generic-constraint, не трогая запросы.

## 4. Согласованность между модулями: события в одной транзакции

Ключевое архитектурное решение, необходимое для соблюдения инвариантов из `ddd-model.md` (например, «`CurrentBalance` кошелька и `BalanceSnapshot` обновляются согласованно с созданием операции», «аудит не должен потерять ни одного изменения»):

1. HTTP-запрос → MediatR-команда (например, `CreateOperationCommand`, реализует `ICommand<TResponse>` — см. §3.1) → **`TransactionBehavior` (BuildingBlocks.Infrastructure) открывает `TransactionScope`** (ambient-транзакция уровня System.Transactions) вокруг всей обработки команды.
2. Обработчик команды в модуле `Operations` сохраняет `Operation` через свой `DbContext.SaveChangesAsync()`, попутно накопив доменные события на агрегате (`OperationCreated`).
3. **`DispatchDomainEventsInterceptor`** (EF Core `SaveChangesInterceptor`, тоже в BuildingBlocks.Infrastructure, подключен к каждому `DbContext` модуля) в хуке `SavedChangesAsync` собирает события со всех агрегатов в `ChangeTracker` и публикует их через MediatR (`DomainEventNotification<T>` / `INotificationHandler`) — то есть диспетчеризация привязана к моменту сохранения конкретного `DbContext`, а не к самому pipeline behavior'у, который лишь держит внешнюю транзакцию открытой.
4. Эффект в других модулях выполняется в рамках **того же самого `TransactionScope`**, каждый через свой `DbContext`, но на тот же физический connection/базу — Npgsql поддерживает несколько `DbContext` в одной транзакции без эскалации к распределенной транзакции, пока все участники работают с одной физической базой. Два разных механизма для этого (уточнено ADR-0001): `Wallets` (обновление `CurrentBalance`) вызывается **синхронно** из кода команды Operations через явный контракт (`IWalletBalanceGateway`), т.к. эффект обязателен для успеха самой команды; `BalanceHistory` (пересчет слепков, ADR-0001) и в будущем `Audit` (запись журнала) — через подписку на доменные события (шаг 3), т.к. это необязательные реакции.
5. Если хотя бы один обработчик падает — откатывается вся транзакция целиком: ни операция, ни баланс, ни слепок, ни аудит не сохранятся частично.

Это дает **сильную консистентность** (не eventual consistency / outbox) ценой того, что вся цепочка обработчиков синхронна и должна укладываться в одну транзакцию БД — приемлемо для однопользовательского приложения с умеренной нагрузкой; при кратном росте нагрузки это первое место, которое потребует пересмотра (см. ADR-0001).

## 5. Фоновые задачи (Hosted Services)

Каждая фоновая задача реализована как `IHostedService`/`BackgroundService` **внутри модуля, которому принадлежит соответствующая доменная операция** — переиспользует те же команды и сервисы, что и пользовательские действия, только с `AuditActor.Kind = System`:

| Модуль | Задача | Периодичность | Решение |
|---|---|---|---|
| `BalanceHistory` | `BalanceSnapshotSchedulerHostedService` — при старте досоздает пропущенные слепки (по одной команде `BackfillWalletBalanceSnapshotsCommand` на кошелек — она вызывает `BalanceRecalculationService`, каскадно закрывает все пропуски разом), затем ежедневно в 12:00 UTC создает/обновляет слепок на сегодня. Обе обязанности — один `BackgroundService`, т.к. обе используют один и тот же `BalanceRecalculationService`. | ежедневно в 12:00 UTC + однократно при старте | раздел 5 требований, ADR-0003 |
| `ExchangeRates` | `ExchangeRateRefreshBackgroundService` — плановое обновление курсов, реакция на смену основного кошелька по сигналу, перезапуск отсчёта после ручного обновления | раз в сутки (+ сброс таймера при ручном обновлении) | ADR-0004 |

Обе задачи идут через команды MediatR в собственном DI-scope с актором `System`. `ExchangeRates` вызывает ту же команду `RefreshExchangeRatesCommand`, что и ручное обновление из API (она намеренно не `ICommand`, см. ADR-0004). `BalanceHistory` отправляет `BackfillWalletBalanceSnapshotsCommand` на каждый кошелёк (`ICommand`) в отдельном scope на кошелёк: слепки кошелька, их аудит и advisory lock пересчёта записываются одной транзакцией `TransactionBehavior`.

## 6. API-хост

Один процесс ASP.NET Core (`LupexWallet.Api`) в `Program.cs` вызывает `AddXxxModule()` / `MapXxxEndpoints()` от каждого модуля (Wallets, ReferenceData, Operations, BalanceHistory, ExchangeRates, Reporting, Audit) — единая точка композиции, единый Swagger/OpenAPI документ на все модули (детали — на этапе «API-контракты»).

## 7. Frontend

Angular SPA — отдельный проект, взаимодействует с бэкендом только через REST API (раздел 9 требований, стек фиксирован). Архитектура самого Angular-приложения (модули, state management) будет проработана отдельно на этапе, где потребуется frontend-код — сейчас фиксируется только факт взаимодействия через REST.

## 8. Аутентификация

Решено: приложение остается однопользовательским без регистрации и ролей (как в бизнес-требованиях), но доступ к API защищен единым паролем.

- Пароль задается на этапе развертывания (конфигурация/переменная окружения), хранится только как хеш (не в открытом виде) — регистрации/смены через публичный эндпоинт на этом этапе не предусмотрено (соответствует «не нужна регистрация»).
- `POST /api/auth/login { password }` — проверяет пароль, при успехе выдает подписанный токен (JWT) с ограниченным сроком действия.
- Все остальные эндпоинты всех модулей требуют заголовок `Authorization: Bearer <token>`; проверка — стандартный ASP.NET Core JWT Bearer middleware в `LupexWallet.Api`, единая точка для всех модулей (не дублируется в каждом).
- Это сквозная (cross-cutting) инфраструктурная забота, не отдельный DDD-модуль/bounded context — в предметной области нет сущности «пользователь» или «сессия» как бизнес-концепции, поэтому логика логина живет в `Host`, а не в `Modules/*`.
- Детали контракта (`/api/auth/login`, формат ответа, срок жизни токена) фиксируются на этапе «API-контракты».

## Связанные материалы

- [ddd-model.md](./ddd-model.md) — источник границ модулей (bounded contexts = модули).
- [ADR-0001](./adr/0001-modular-monolith.md) — фиксирует решения этого документа как архитектурное решение.
