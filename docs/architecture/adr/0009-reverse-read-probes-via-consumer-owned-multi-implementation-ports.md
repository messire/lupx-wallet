# ADR-0009: Обратные кросс-модульные read-контракты через порты, объявленные у потребителя, с множественной реализацией у владельцев данных

## Статус

Принято

## Контекст

`ddd-model.md` и фактический граф зависимостей проектов (см. `docs/PROGRESS.md`, раздел «Текущий граф зависимостей проектов») зафиксировали направление «снизу вверх»: `Wallets → Operations → BalanceHistory`, а также `ReferenceData` как независимый модуль, от которого зависит `Operations`. ADR-0007 и ADR-0008 уже установили прецедент для связей **по этому направлению**: узкий read-контракт объявляется в `Application`-проекте потребителя (например, `Operations.Application.IWalletBalanceGateway` — нет, уточнение: контракт объявлен в `Application` того модуля, чьи данные читаются/меняются, т.е. `Wallets.Application.IWalletBalanceGateway`, а вызывает его `Operations.Application`), реализация — в `Infrastructure` модуля-владельца данных, регистрация — в `Add<Owner>Module`.

При планировании оставшегося объёма (`docs/PROGRESS.md`, «ВОЛНА 0», пункт W0.1) обнаружены три места, где нужна связь в **обратном** направлении — «нижний» по графу модуль должен спросить «верхний»:

| # | Кто спрашивает | Что нужно узнать | У кого (владелец данных) |
|---|---|---|---|
| a | `Wallets.Application` | есть ли у кошелька операции/слепки (`hasHistory` для `DELETE /wallets/{id}` и `PUT /wallets/{id}/currency`, Q2/Q15) | `Operations`, `BalanceHistory` |
| b | `Operations.Application` | дата последнего слепка кошелька (ADR-0002, `DeleteOperationCommand`/`DeleteTransferCommand`) | `BalanceHistory` |
| c | `ReferenceData.Application` | используется ли элемент справочника (`isUsed`, Q11) | `Wallets`, `Operations`, `ExchangeRates` |

Прямая ссылка невозможна: `Operations.Application → Wallets.Application` уже существует (ADR-0007), поэтому обратная ссылка `Wallets.Application → Operations.Application` образовала бы цикл между теми же двумя проектами; аналогично для `BalanceHistory.Application → Operations.Application` (уже есть, ADR-0008) — обратная ссылка `Operations.Application → BalanceHistory.Application` для случая (b) была прямо зафиксирована как заблокированная в `docs/PROGRESS.md` («Известные упрощения»).

Два случая (a) и (c) дополнительно осложнены тем, что ответ должен собираться **из нескольких владельцев данных** (для a: и Operations, и BalanceHistory; для c: Wallets, Operations и в будущем ExchangeRates) — то есть недостаточно одной пары «интерфейс/реализация», нужна композиция нескольких источников без нарушения границы модуля.

## Решение

Применяется **тот же приём, что уже используют `IWalletBalanceGateway`/`IWalletDirectory`/`IWalletOperationsLookup` (ADR-0007/ADR-0008): порт объявляется в `Application`-проекте того модуля, который задаёт вопрос (потребителя), реализация — в `Infrastructure`-проекте модуля, который физически владеет данными для ответа**. Ссылка всегда идёт от Infrastructure владельца к Application потребителя — то есть в том же направлении, в котором этот владелец и так уже зависит от потребителя (см. «Проверка на циклы» ниже), поэтому нового цикла не возникает ни в одном из трёх случаев.

Отличие от уже реализованных прецедентов — **множественная реализация одного порта**, когда данных, отвечающих на вопрос, у нескольких модулей-владельцев. Вместо составного (composite) сервиса в отдельном проекте или в Host-е используется прямая DI-композиция: каждый владелец регистрирует **свою** реализацию под тем же интерфейсом, потребитель внедряет `IEnumerable<TPort>` и агрегирует ответы сам (`AnyAsync`/`OrElse` — логика зависит от семантики вопроса). Это не требует ни новых проектов, ни составного адаптера в Host — DI-контейнер сам складывает регистрации из разных `Add<Owner>Module` в один список.

### Случай (a): `hasHistory` кошелька

- Порт: **`Wallets.Application.IWalletHistorySource`** — `Task<bool> HasHistoryAsync(WalletId walletId, CancellationToken ct)`.
- Реализации:
  - `Operations.Infrastructure.OperationsWalletHistorySource` — `true`, если у кошелька есть хотя бы одна `Operation` (включая обе «ноги» переводов). Регистрируется в `AddOperationsModule`.
  - `BalanceHistory.Infrastructure.BalanceHistoryWalletHistorySource` — `true`, если у кошелька есть хотя бы один `BalanceSnapshot`. Регистрируется в `AddBalanceHistoryModule`.
- Потребитель: `DeleteWalletCommandHandler` / `ChangeWalletCurrencyCommandHandler` (`Wallets.Application`) внедряют `IEnumerable<IWalletHistorySource>`, `hasHistory = await AnyAsync(...)` по всем зарегистрированным источникам.

### Случай (b): «история» операции/перевода для удаления — ЗАКРЫТ БЕЗ ПОРТА (решение пользователя, 2026-09-11)

Изначально планировался порт `Operations.Application.IWalletSnapshotBoundary` (`Task<DateOnly?> GetLastSnapshotDateAsync(...)`) с единственной реализацией `BalanceHistory.Infrastructure.BalanceHistoryWalletSnapshotBoundary`, по аналогии с 1:1-портами ADR-0008. При реализации (W2.5) обнаружилось, что буквальная проверка UC-14 «дата операции ≤ дате последнего слепка» непригодна: каскадный пересчет (ADR-0003/ADR-0008) материализует слепок кошелька на дату **самой** операции немедленно при её создании/изменении — то есть «последний слепок» почти всегда ≥ дате любой существующей операции, и буквальное применение заблокировало бы подавляющее большинство удалений, включая только что созданную операцию (прямо противоречит UC-14).

**Решение пользователя (2026-09-11)**: граница «история» для `DELETE /operations/{id}` и `DELETE /transfers/{id}` — **сегодняшний день**, без обращения к `BalanceHistory`. Операция/перевод с датой = сегодня — можно удалить всегда (при отсутствии прочих препятствий, например участия в переводе); с датой строго раньше сегодня — нельзя (только редактирование, UC-13). Проверка выполняется локально в `Operations.Application` (`DateOnly.FromDateTime(DateTime.UtcNow)`, тот же паттерн, что уже использует `OperationDateValidator`), кросс-модульный запрос к `BalanceHistory` не понадобился.

Следствие: `Operations.Application.IWalletSnapshotBoundary` и `BalanceHistory.Infrastructure.BalanceHistoryWalletSnapshotBoundary` **не существуют** — этот случай не является примером паттерна «порт у потребителя / реализация у владельца», описанного этим ADR для случаев (a) и (c); он оставлен в этом документе как зафиксированная история решения (почему кросс-модульный контракт был предусмотрен, но не понадобился).

### Случай (c): `isUsed` элемента справочника

- Порт: **`ReferenceData.Application.IReferenceItemUsageProbe`** — `Task<bool> IsUsedAsync(ReferenceItemKind kind, Guid referenceItemId, CancellationToken ct)`, где `ReferenceItemKind` (enum в `ReferenceData.Application`) = `WalletType | OperationType | Currency`. Реализация, которой конкретный `kind` не релевантен, возвращает `false` для него (например, `BalanceHistory` не хранит ссылок на элементы справочника вовсе и этот порт не реализует).
- Реализации:
  - `Wallets.Infrastructure.WalletsReferenceItemUsageProbe` — проверяет `WalletType`/`Currency` по факту использования в `wallets.wallets`. Регистрируется в `AddWalletsModule`.
  - `Operations.Infrastructure.OperationsReferenceItemUsageProbe` — проверяет `OperationType`/`Currency` по `operations.operations`. Регистрируется в `AddOperationsModule`.
  - `ExchangeRates.Infrastructure.ExchangeRatesReferenceItemUsageProbe` — проверяет `Currency` по факту использования в `exchange_rates.*` (появится вместе с W1.1; до этого момента `Currency` считается неиспользуемой этим источником — это корректно, т.к. модуля физически ещё нет).
- Потребитель: `DeleteWalletTypeCommandHandler` / `DeleteOperationTypeCommandHandler` / `DeleteCurrencyCommandHandler` (`ReferenceData.Application`) внедряют `IEnumerable<IReferenceItemUsageProbe>`.

### Проверка на циклы

Ссылка каждой реализации идёт **в том же направлении**, в котором её модуль уже зависит от потребителя:
- `Operations` уже ссылается на `Wallets.Application` (ADR-0007) → `Operations.Infrastructure → Wallets.Application` (случай a, источник Operations) не новый по направлению.
- `BalanceHistory` уже ссылается на `Wallets.Application` и `Operations.Application` (ADR-0008) → `BalanceHistory.Infrastructure → Wallets.Application` (случай a) не новое по направлению. Случай (b) в итоге не потребовал такого ребра вовсе — см. раздел выше.
- `Wallets`/`Operations`/`ExchangeRates` не зависят от `ReferenceData` по кругу — `Operations.Application` уже ссылается на `ReferenceData.Application` (ADR-0007, `IOperationTypeLookup`), поэтому `Operations.Infrastructure → ReferenceData.Application` (случай c) — тоже не новое направление; `Wallets.Infrastructure → ReferenceData.Application` и `ExchangeRates.Infrastructure → ReferenceData.Application` — новые рёбра, но однонаправленные (`ReferenceData` ни на кого не ссылается), цикла не образуют.

Ни в одном из трёх случаев `*.Domain` не участвует — только `Application` (порт) и `Infrastructure` (реализация), что не расширяет правило границы модуля, а использует уже существующий третий разрешённый способ связи (`high-level-architecture.md`, §2, п.3).

### Обязательное условие: пустой список реализаций — ошибка, не «false»

Для каждого порта фиксируется **ожидаемое число реализаций**, известное на этапе компоновки Host (по количеству модулей-владельцев, перечисленных выше: 2 для случая a, ≥2 для случая c, растёт при появлении новых владельцев данных элементов справочника; случай b не использует эту схему — см. раздел выше). W2.5 обязан обеспечить, чтобы отсутствие ожидаемой реализации в DI (например, забыли вызвать `Add<Owner>Module` или забыли зарегистрировать конкретный пробник) приводило к **явному отказу при старте хоста** (fail-fast: hosted-check/тест на количество зарегистрированных `IEnumerable<TPort>.Count()`), а не к молчаливому `hasHistory=false`/`isUsed=false` — иначе регресс в регистрации незаметно откроет удаление кошелька/справочника, у которого фактически есть история/использование. Конкретный механизм проверки (стартовый guard vs. интеграционный тест, фиксирующий количество регистраций) выбирает реализующий агент W2.5; это условие DoD, а не деталь реализации ADR.

## Последствия

- Единый приём для всех трёх случаев (и для любых будущих обратных read-запросов): «порт у потребителя, адаптер(ы) у владельца(ев), агрегация — через `IEnumerable<TPort>` на стороне потребителя» — не вводит новых проектов, не меняет правило границы модуля из `high-level-architecture.md` §2, а уточняет его: пункт 3 («явно опубликованный узкий read-only контракт») допускает множественную регистрацию одного порта разными модулями-владельцами.
- `Wallets.Infrastructure`/`Operations.Infrastructure`/`BalanceHistory.Infrastructure`/`ExchangeRates.Infrastructure` получают новые `ProjectReference` на `Application`-проекты модулей-потребителей (перечислены в «Проверка на циклы» выше) — все они добавляют рёбра в направлении, где зависимость уже существует на уровне `Application ↔ Application`, поэтому дополнительной связности между модулями, которой не было бы и так, не возникает.
- Логика агрегации (`AnyAsync` по нескольким источникам) живёт в `Application`-обработчике команды потребителя — не размазывается по Infrastructure и не требует составного сервиса в Host.
- Появление нового владельца данных для существующего порта (например, будущий модуль, тоже хранящий операции по кошельку) — это только новая реализация + одна строка регистрации в его `Add*Module`; интерфейс и потребитель не меняются. Ожидаемое число реализаций (см. выше) необходимо обновить вручную — это единственная точка, требующая ручного сопровождения при росте числа владельцев.
- Раздел «Известные упрощения» `docs/PROGRESS.md` (кросс-модульная валидация ссылок для случаев a и c) закрывается реализацией W2.5 по этому ADR; случай (b) закрыт отдельно, без кросс-модульного порта (см. раздел выше и решение пользователя от 2026-09-11).

## Альтернативы

- **Вариант B — отдельные `*.Contracts`-проекты у владельцев данных** (`LupexWallet.BalanceHistory.Contracts`, `LupexWallet.Operations.Contracts`, `LupexWallet.Wallets.Contracts`, зависящие только от `SharedKernel`). Отклонено: не соответствует уже устоявшейся практике проекта (`IWalletBalanceGateway`/`IWalletDirectory`/`IWalletOperationsLookup`/`IOperationTypeLookup` — все объявлены в `Application` потребителя, ни один не вынесен в отдельный `Contracts`-проект), добавило бы 3 новых проекта из 30 ради консистентности, которую вариант A с `IEnumerable<TPort>`-агрегацией достигает без новых проектов, и создало бы два параллельных стиля объявления контрактов в одной кодовой базе (старый — в Application, новый — в Contracts) без плана миграции существующих. Исходная причина, по которой планировщик рассматривал вариант B (сложность композиции нескольких источников для случаев a/c), снята агрегацией через `IEnumerable<TPort>` — она не требует ни составного адаптера, ни отдельного проекта.
- **Составной (`Composite*`) адаптер в Host**, вручную перечисляющий источники (`new CompositeWalletHistorySource(operationsSource, balanceHistorySource)`), вместо `IEnumerable<TPort>`. Отклонено: DI-контейнер и так умеет резолвить `IEnumerable<TPort>` из нескольких регистраций без ручной сборки в Host — составной адаптер добавил бы код без выигрыша и потребовал бы правки Host при каждом новом владельце (тогда как `IEnumerable<TPort>` подхватывает новую регистрацию автоматически).
- **Событие вместо синхронного порта** (например, `WalletDeletionRequested` → подписчики Operations/BalanceHistory синхронно проверяют и могут отменить). Отклонено: по критерию ADR-0007 («эффект обязателен для успеха самой команды» → синхронный контракт, а не событие) — результат `hasHistory`/`isUsed`/«дата последнего слепка» обязателен для корректности самой команды (иначе она выполнится ошибочно), а не является необязательной реакцией.

## Связанные материалы

- [ADR-0006](./0006-modular-monolith-boundaries.md) — границы модульного монолита.
- [ADR-0007](./0007-synchronous-cross-module-gateways.md) — прецедент «порт у потребителя, адаптер у владельца» для направления «снизу вверх», критерий синхронный контракт vs. событие.
- [ADR-0008](./0008-balance-history-recalculation-mechanism.md) — второй прецедент того же приёма (`IWalletOperationsLookup`, `IWalletDirectory`).
- `high-level-architecture.md`, §2 (правило границы модуля, пункт 3, уточнён этим ADR).
- `docs/PROGRESS.md`, «ВОЛНА 0», W0.1 — постановка задачи и DoD; «ВОЛНА 2», W2.5 — реализация по этому ADR.
