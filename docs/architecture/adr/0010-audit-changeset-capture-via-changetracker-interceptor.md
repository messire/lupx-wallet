# ADR-0010: Получение diff old/new для аудита — гибрид EF Core ChangeTracker + точечное обогащение

## Статус

Принято

## Контекст

`ddd-model.md` (§2.8, §5 "AuditEntry") и `docs/database/schema.md` (`audit.audit_entries.changes jsonb`) требуют, чтобы каждая запись аудита содержала значения полей **до/после** (`AuditFieldChange`), а не только факт изменения. Существующие доменные события всех модулей (`WalletUpdated(WalletId)`, `OperationCreated(OperationId, WalletId, OperationDate)` и т.д.) несут только идентификаторы — они спроектированы под нужды BalanceHistory (ADR-0008), которому диффы полей не требовались. `docs/PROGRESS.md` (план, W2.2) сформулировал три варианта:

1. Обогатить сами доменные события полями old/new.
2. Снимать diff из EF Core `ChangeTracker` отдельным `SaveChangesInterceptor`, не трогая Domain.
3. Гибрид (2) + точечное обогащение там, где diff из ChangeTracker бессмыслен.

## Решение

Выбран вариант **(3), гибрид**, реализован как продолжение `DispatchDomainEventsInterceptor` (BuildingBlocks.Infrastructure), а не отдельный второй интерцептор — оба должны видеть один и тот же `ChangeTracker` в один и тот же момент (`SavingChangesAsync`, до физической отправки SQL), и агрегаты с событиями уже перечисляются в существующем методе `CollectEvents`.

### 1. Базовый механизм — diff из ChangeTracker, привязанный к `EventId`

`DispatchDomainEventsInterceptor.CollectEvents` (вызывается в `SavingChangesAsync`, **до** `SaveChanges`, когда `EntityEntry.OriginalValues`/`CurrentValues` ещё различаются) для каждого отслеживаемого агрегата с непубликованными событиями дополнительно вычисляет список `EntityFieldChange(FieldName, OldValue, NewValue)` по его собственным (не навигационным, не ключевым) свойствам:

- `EntityState.Added` → `OldValue = null`, `NewValue = CurrentValue` для всех полей (создание).
- `EntityState.Deleted` → `OldValue = OriginalValue`, `NewValue = null` (полный снимок удаляемой записи).
- `EntityState.Modified` → только свойства с `IsModified = true`, `OldValue = OriginalValue`, `NewValue = CurrentValue`.

Результат сохраняется в `Dictionary<Guid /*EventId*/, IReadOnlyList<EntityFieldChange>>` **внутри того же экземпляра интерцептора**, ключ — `IDomainEvent.EventId` (уже существует на базовом `DomainEvent`, не требует изменений Domain). Один и тот же расчётный diff агрегата присваивается **каждому** событию, которое этот агрегат поднял за один `SaveChanges` (в норме — одно событие; для `Wallet.Create` первого кошелька их два — `WalletCreated` и `PrimaryWalletChanged` — оба получают одинаковый diff "все поля из null", что корректно отражает создание).

Диспетчеризация событий по-прежнему происходит в `SavedChangesAsync` (после физического сохранения, ADR-0008) — но diff уже вычислен и лежит в словаре по `EventId`, поэтому подписчик Audit, вызываемый на этом шаге, может забрать его методом `interceptor.TakeChanges(eventId)` (удаляет запись из словаря — предотвращает рост словаря в рамках долгого scope с массовым каскадным пересчётом, риск №3 плана, см. ниже).

Интерцептор — **общий экземпляр на весь DI-scope** между всеми `DbContext` всех модулей (уже задокументировано в ADR-0008/коде) — поэтому diff, вычисленный при сохранении, скажем, `WalletsDbContext`, корректно виден обработчику Audit, даже если тот использует свой `AuditDbContext` в рамках диспетчеризации, вызванной из `SavedChangesAsync` именно `WalletsDbContext`.

Почему не отдельный интерцептор специально для аудита: пришлось бы дважды проходить `ChangeTracker` в двух разных, потенциально по-разному упорядоченных интерцепторах на одном `DbContext`, и синхронизировать два независимых способа "приклеить diff к событию" — избыточно, раз `DispatchDomainEventsInterceptor` и так уже итерирует те же агрегаты с теми же событиями в том же хуке.

### 2. Точечное обогащение — где ChangeTracker-diff бессмыслен

Три случая, где Audit **не** использует `interceptor.TakeChanges`, а формирует `changes` вручную из полей самого события (обработчики в `Audit.Infrastructure` вызывают `AuditRecorder.RecordAsync(...)` вместо `RecordFromChangeTrackerAsync(...)`):

- **`BalanceSnapshotCreated` / `BalanceSnapshotUpdated`.** `BalanceSnapshot` идентифицируется составным ключом `(WalletId, SnapshotDate)` без суррогатного `Id` (см. `BalanceSnapshot.cs`) — `SnapshotDate` при этом часть первичного ключа, а базовый механизм диффа **исключает** ключевые свойства (они не "изменяются", они и есть идентичность записи). Если использовать общий механизм, `changes` содержал бы только `Balance`, но не `SnapshotDate` — при каскадном пересчёте нескольких дат одного кошелька в одной транзакции все получившиеся `AuditEntry` были бы неотличимы друг от друга по `changes`. Обработчик вручную кладёт в `changes` обе пары `SnapshotDate`/`Balance` из полей события (`BalanceSnapshotCreated`/`BalanceSnapshotUpdated` уже несут `Balance`/`OldBalance`/`NewBalance` — обогащать сам домен не потребовалось).
- **`ExchangeRatesUpdated` / `ExchangeRateUpdateFailed`.** События батчевые — описывают результат целого прогона обновления (несколько валютных пар), не привязаны к одному агрегату/одной строке `ChangeTracker` (см. комментарий в `ExchangeRatesEvents.cs`). `changes` формируется как список пар "FromCurrencyId→ToCurrencyId" с новым курсом (успех) или причиной отказа (`ExchangeRateUpdateFailed`); `entityId` — фиксированный `Guid.Empty` (нет единственной строки-владельца события, но `schema.md` требует непустой `entity_id`). **Известное упрощение**: валюты представлены как `CurrencyId` (Guid), не как код валюты (`USD`) — обогащение через `ICurrencyLookup` не выполнено в этом срезе ради простоты; при необходимости читаемых кодов в UI (W3.2) это можно добавить отдельным точечным изменением обработчика, не трогая механизм.

Оба случая совпадают с тем, что было явно предсказано в плане (`docs/PROGRESS.md`, п.4 "ExchangeRateUpdateFailed"), плюс `BalanceSnapshot*` добавлен по факту анализа его составного ключа при реализации — того же рода исключение, не новое архитектурное решение.

### 3. Проблема курсорной пагинации `/audit-entries` — монотонные метки времени вместо диффа Id

Курсор `GET /audit-entries` сортирует `occurred_at DESC`. Прежний прецедент проекта (`WalletCursor`, `OperationCursor`, `NameCursor`) для устранения неоднозначности при совпадающих значениях основного поля сортировки использует пару `(поле, CreatedAt)`, а не типизированный `Id` — EF Core не транслирует member-access `x.Id.Value` в LINQ-выражениях (задокументированный факт этого проекта, `docs/PROGRESS.md`, "Баги, найденные и исправленные"). Для `AuditEntry` второго уникального поля, кроме `Id`, нет — `occurred_at` единственный кандидат, и **может совпасть** у нескольких записей одной транзакции (например, каскадный пересчёт истории баланса порождает много `BalanceSnapshotUpdated` быстрее разрешения системных часов, риск №3 плана).

Решение: `AuditEntry.Create` берёт `OccurredAt` не из `DateTimeOffset.UtcNow` напрямую, а из `MonotonicClock.UtcNow()` (Audit.Domain, без внешних зависимостей) — вспомогательного счётчика, гарантирующего **строго возрастающие** метки времени в пределах процесса (при коллизии тиков подставляется `последний_тик + 1`, `Interlocked.CompareExchange`). Практически неотличимо от реального времени (сдвиг на наносекунды при коллизии), но гарантирует, что `occurred_at` можно использовать как единственное поле курсора без дублей/пропусков между страницами (`AuditEntryCursor(DateTimeOffset OccurredAt)`, `WHERE occurred_at < @cursor`, `ORDER BY occurred_at DESC`) — без введения дополнительной колонки, отсутствующей в `schema.md`, и без завязки на `Id`.

### 4. Политика объёма аудита при каскадном пересчёте (риск №3 плана)

`ddd-model.md` §6 явно требует: "по одной записи на каждый обновлённый слепок" — это зафиксированное бизнес-требование (раздел 6 исходных требований: "обновление слепка баланса" — отслеживаемое действие), а не техническая деталь, которую можно менять по инженерному усмотрению (`CLAUDE.md`: "не менять бизнес-требования молча"). Решение — **оставить как есть**: одна `AuditEntry` на каждое событие `BalanceSnapshotUpdated`/`BalanceSnapshotCreated`, в той же транзакции, что и сам пересчёт.

Смягчение риска (рост объёма журнала на кошельках с длинной историей) — на уровне реализации, не бизнес-политики:
- `interceptor.TakeChanges(eventId)` удаляет запись из словаря сразу при чтении — словарь не растёт сверх количества **ещё не обработанных** событий одной транзакции (обрабатываются последовательно, не накапливаются на весь scope).
- Запись `AuditEntry` — один `INSERT` на событие (не пакетный `bulk copy`) — при типичном объёме кошелька (десятки-сотни пересчитываемых дат, не десятки тысяч) не является узким местом на фоне уже существующей стоимости каскадного пересчёта самого `BalanceRecalculationService` (тоже одна операция на дату). Если это когда-либо станет проблемой производительности — пересмотр политики (агрегированная запись на прогон) потребует **изменения бизнес-требования** (ddd-model §6) и явного запроса пользователя, а не одностороннего решения агента.

## Механизм актора (User/System)

`IAuditActorAccessor` (BuildingBlocks.Infrastructure, Scoped) — `AuditActorContext { bool IsSystem, string? SystemProcessName }`, по умолчанию `User` (не-System). ASP.NET Core создаёт новый DI-scope на каждый HTTP-запрос — свежий `AuditActorAccessor` уже "User" без дополнительного middleware. Оба фоновых сервиса (`BalanceSnapshotSchedulerHostedService`, `ExchangeRateRefreshBackgroundService`) создают **собственный** `IServiceScope` на каждый проход (уже делали это до этого ADR) и теперь первым действием в нём вызывают `accessor.SetSystemActor(nameof(<Сервис>))` — до отправки MediatR-команды. Каскадный пересчёт истории баланса, вызванный синхронно из HTTP-запроса пользователя (`OperationEventHandlers` в том же scope, что и исходная команда), корректно наследует "User" — только сам плановый/досоздающий прогон `BalanceSnapshotSchedulerHostedService` помечен как "System".

`IAuditActorAccessor` живёт в `BuildingBlocks.Infrastructure`, а не в `Audit.Application`, потому что его читают/пишут `Audit.Infrastructure` **и** `BalanceHistory.Infrastructure`/`ExchangeRates.Infrastructure` (фоновые сервисы) — размещение в `Audit.Application` потребовало бы от них ссылки на модуль `Audit`, которой в графе зависимостей проекта не должно быть (`high-level-architecture.md`, §2, правило границы модуля). Размещение по аналогии с `IDomainEventDispatcher` — общая инфраструктурная абстракция, не специфичная бизнес-логика модуля Audit.

## Тестовая инфраструктура: `audit.audit_entries` и Respawn

`CREATE RULE audit_entries_no_update/no_delete ... DO INSTEAD NOTHING` (schema.md) делает `DELETE FROM audit.audit_entries`, который генерирует Respawn при обычном сбросе, безрезультатным no-op (PostgreSQL превращает `DELETE` в ничего не делающую операцию, но не возвращает ошибку) — данные аудита накапливались бы между тестами молча. Решение: схема `audit` **не** включена в `RespawnerOptions.SchemasToInclude` (`ApiTestFixture`); вместо этого `ApiTestFixture.ResetAsync()` дополнительно выполняет `TRUNCATE TABLE audit.audit_entries;` явным raw-SQL после `_respawner.ResetAsync(...)`. `TRUNCATE` не перехватывается `CREATE RULE` (PostgreSQL не поддерживает `ON TRUNCATE` для `RULE` — задокументированное ограничение самого `CREATE RULE`), поэтому очищает таблицу как обычно, не нарушая неизменяемость через `DELETE`/`UPDATE` в проде.

## Альтернативы

- **(1) Обогатить доменные события полями old/new** — отклонено: требует правок `*.Domain` всех пяти модулей-издателей (конфликт по файлам с параллельными волнами плана, `CLAUDE.md`/`docs/PROGRESS.md` явно предостерегают от раздувания событий под нужды одного-единственного подписчика, когда есть способ не трогать Domain вовсе).
- **(2) Чистый ChangeTracker-diff без исключений** — отклонено как единственный механизм: не работает для `BalanceSnapshot` (составной ключ теряет `SnapshotDate` из diff) и `ExchangeRates*` (нет единого агрегата на событие) — привело бы либо к неинформативным записям аудита, либо к обходному изменению домена (тому же варианту 1) специально под эти два случая.
- **Отдельный `AuditCaptureInterceptor`** — отклонено: дублирует итерацию `ChangeTracker.Entries<IHasDomainEvents>()`, уже выполняемую `DispatchDomainEventsInterceptor`, без выигрыша в разделении ответственности (оба должны согласованно работать с одним и тем же набором событий/агрегатов в одном и том же хуке).
- **Тиебрейкер по `Id` вместо монотонных меток времени** — отклонено: типизированный `AuditEntryId` не поддерживает транслируемое в SQL сравнение `<`/`>` (тот же класс проблем, что и `w.Id.Value` в остальных модулях), а добавление отдельного `IComparable`/сырого `Guid`-компаратора не решает именно проблему LINQ-трансляции.

## Последствия

- `Audit.Infrastructure` ссылается на `*.Domain` пяти модулей-издателей (Wallets, ReferenceData, Operations, BalanceHistory, ExchangeRates) ради типов их доменных событий, на которые подписан (не ради бизнес-логики) — тот же паттерн, что уже применён в ADR-0008 (`BalanceHistory.Infrastructure → Operations.Domain`) и ADR-0011 (`ExchangeRates.Infrastructure → Wallets.Domain`); зафиксирован как 4-й разрешённый способ связи между модулями в `high-level-architecture.md`, §2.
- `EntityFieldChange` (BuildingBlocks.Infrastructure) — новый универсальный тип, не зависящий от Audit; в будущем может быть переиспользован другими подписчиками, которым тоже понадобится diff (не только Audit).
- `DispatchDomainEventsInterceptor` расширен новой обязанностью (расчёт diff) — задокументировано в самом файле; поведение диспетчеризации событий (ADR-0008) не изменилось.
- Диффы для `BalanceSnapshot*`/`ExchangeRates*` обслуживаются вручную в обработчиках Audit — при появлении новых событий с похожей структурой (составной ключ без суррогата или батчевые события) следует явно решать по аналогии, а не молча полагаться на `RecordFromChangeTrackerAsync`.
- Формат `changes` для полей, отображённых через shadow-свойства EF (например, `Wallet._initialBalanceAmount`) содержит внутреннее имя поля EF (с подчёркиванием), а не публичное доменное имя (`InitialBalance`) — известное упрощение, не блокирует DoD (диффы корректны по значениям, только имя поля менее «причёсано»); при необходимости более читаемых имён в UI (W3.2) потребуется явное сопоставление имён свойств, не входит в этот срез.

## Связанные материалы

- [ADR-0008](./0008-balance-history-recalculation-mechanism.md) — `DispatchDomainEventsInterceptor`, пост-save диспетчеризация, общий экземпляр интерцептора на DI-scope.
- [ADR-0009](./0009-reverse-read-probes-via-consumer-owned-multi-implementation-ports.md) — общий стиль решений про межмодульные абстракции без раздувания графа проектов.
- `ddd-model.md`, §2.8 (`AuditEntry`), §4 (`AuditActor`/`AuditFieldChange`), §5 (инвариант неизменяемости и обязательности записи), §6 (таблица реакций на команды всех модулей, политика "запись на слепок").
- `docs/database/schema.md`, раздел "Схема `audit`".
