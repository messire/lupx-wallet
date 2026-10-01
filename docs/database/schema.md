# Схема базы данных (PostgreSQL)

Источники: [ddd-model.md](../architecture/ddd-model.md), [high-level-architecture.md](../architecture/high-level-architecture.md), ADR-0001…ADR-0006.

Технические идентификаторы (схемы, таблицы, колонки) — на английском (`snake_case`, идиоматично для PostgreSQL); пояснения — на русском.

## Общие соглашения

- **Схема на модуль**, согласно ADR-0001: `wallets`, `reference_data`, `operations`, `balance_history`, `exchange_rates`, `audit`. `Reporting` собственной схемы не имеет (только читает).
- **Без внешних ключей между схемами разных модулей.** Ссылка на сущность другого модуля хранится как обычная колонка `uuid` (например, `operations.wallet_id`), без `REFERENCES` на чужую схему — ссылочная целостность обеспечивается на уровне приложения (ADR-0001). Внешние ключи **внутри одной схемы** (например, `operation_types.behavior_kind_id → reference_data.operation_behavior_kinds.id`) используются как обычно.
- **Идентификаторы** — `uuid`, генерируются на стороне приложения (не `gen_random_uuid()` в БД), чтобы агрегат мог знать свой Id сразу после создания в памяти, до сохранения (типично для .NET/EF Core). Рекомендуется генератор с сортируемым префиксом (например, UUIDv7) для лучшей локальности индекса — конкретная библиотека выбирается на этапе реализации, не фиксируется здесь.
- **Денежные суммы и курсы** — тип `numeric` без фиксированных precision/scale (произвольная точность, ADR-0005). Округление — только на уровне отображения (UI), не хранения.
- **Даты** — два разных типа осознанно: `date` для бизнес-дат (`operation_date`, `snapshot_date`, `rate_date`, `accounting_start_date` — это календарные даты без времени и часового пояса, так задумано доменной моделью), `timestamptz` для технических моментов времени (`created_at`, `occurred_at`, `fetched_at`).
- **Конкурентный доступ** — оптимистичная блокировка через системную колонку PostgreSQL `xmin`, отображаемую в EF Core как `[Timestamp]`/`IsRowVersion()`; отдельная колонка `row_version` не заводится.
- **Именование** — таблицы и колонки в `snake_case`; EF Core сопоставляет их с PascalCase-сущностями через `EFCore.NamingConventions` (техническая деталь реализации, не бизнес-решение).

## Схема `reference_data`

```sql
CREATE SCHEMA reference_data;

CREATE TABLE reference_data.operation_behavior_kinds (
    id          uuid PRIMARY KEY,
    code        varchar(32)  NOT NULL UNIQUE,  -- 'Income' | 'Expense' | 'Transfer' | 'Adjustment'
    name        varchar(200) NOT NULL,
    created_at  timestamptz  NOT NULL DEFAULT now(),
    updated_at  timestamptz  NOT NULL DEFAULT now()
);
-- Строки сеются один раз миграцией (Income/Expense/Transfer/Adjustment); без CRUD через API/UI (ddd-model.md, §2.3).

CREATE TABLE reference_data.wallet_types (
    id          uuid PRIMARY KEY,
    name        varchar(200) NOT NULL,
    is_active   boolean      NOT NULL DEFAULT true,
    created_at  timestamptz  NOT NULL DEFAULT now(),
    updated_at  timestamptz  NOT NULL DEFAULT now()
);
-- Уникальность имени только среди активных — позволяет переиспользовать имя после деактивации старого элемента.
CREATE UNIQUE INDEX ux_wallet_types_active_name
    ON reference_data.wallet_types (lower(name)) WHERE is_active;

CREATE TABLE reference_data.operation_types (
    id                 uuid PRIMARY KEY,
    name               varchar(200) NOT NULL,
    behavior_kind_id   uuid         NOT NULL REFERENCES reference_data.operation_behavior_kinds(id),
    is_active          boolean      NOT NULL DEFAULT true,
    created_at         timestamptz  NOT NULL DEFAULT now(),
    updated_at         timestamptz  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_operation_types_active_name
    ON reference_data.operation_types (lower(name)) WHERE is_active;
CREATE INDEX ix_operation_types_behavior_kind ON reference_data.operation_types (behavior_kind_id);

-- Активные валюты пользователя (ADR-0004, §7). Наличие строки = валюта активна; колонки is_active нет
-- (состояния «деактивирована» у валюты нет; колонка удалена миграцией AvailableCurrenciesAndNoDeactivation).
CREATE TABLE reference_data.currencies (
    id          uuid PRIMARY KEY,
    code        varchar(16)  NOT NULL,   -- напр. 'USD'; код из списка доступных валют (available_currencies), проверяется приложением, без FK
    name        varchar(200) NOT NULL,   -- английское название сервиса; синхронизируется при обновлении списка доступных
    created_at  timestamptz  NOT NULL DEFAULT now(),
    updated_at  timestamptz  NOT NULL DEFAULT now()
);
-- Код валюты уникален (регистронезависимо) — не более одной активной валюты на код.
CREATE UNIQUE INDEX ux_currencies_code ON reference_data.currencies (upper(code));

-- Список доступных валют — копия списка сервиса курсов (ADR-0004, §7). Пользователем не управляется;
-- заменяется целиком при каждом обновлении курсов (upsert по code, отсутствующие в ответе удаляются).
-- Нужен только для списков выбора и проверки «код есть среди доступных». Без FK на currencies в обе
-- стороны: валюта, исчезнувшая из сервиса, остается активной; ссылка идет по коду.
CREATE TABLE reference_data.available_currencies (
    code          varchar(16)  PRIMARY KEY,
    name          varchar(200) NOT NULL,   -- английское название из сервиса
    refreshed_at  timestamptz  NOT NULL    -- момент последнего обновления списка, затронувшего строку
);
```

## Схема `wallets`

```sql
CREATE SCHEMA wallets;

CREATE TABLE wallets.wallets (
    id                      uuid PRIMARY KEY,
    name                    varchar(200) NOT NULL,
    wallet_type_id          uuid         NOT NULL,   -- reference_data.wallet_types.id, без FK (ADR-0001)
    purpose_description     text,
    currency_id             uuid         NOT NULL,   -- reference_data.currencies.id, без FK
    initial_balance_amount  numeric      NOT NULL,
    accounting_start_date   date         NOT NULL,
    current_balance_amount  numeric      NOT NULL,   -- кэш, синхронизируется событиями Operations (ddd-model.md §2.1)
    include_in_total        boolean      NOT NULL DEFAULT true,
    is_primary              boolean      NOT NULL DEFAULT false,
    is_archived             boolean      NOT NULL DEFAULT false,
    display_order           integer      NOT NULL DEFAULT 0,
    color                   varchar(32),
    icon                    varchar(64),
    created_at              timestamptz  NOT NULL DEFAULT now(),
    updated_at              timestamptz  NOT NULL DEFAULT now(),

    -- инвариант Q7/Q8: основной кошелек не может быть архивным и обязан быть включен в общую сумму
    CONSTRAINT ck_wallets_primary_not_archived CHECK (NOT (is_primary AND is_archived)),
    CONSTRAINT ck_wallets_primary_included_in_total CHECK (NOT is_primary OR include_in_total)
);

-- инвариант Q7: в системе не более одного основного кошелька (в сочетании с прикладной логикой — гарантированно ровно один)
CREATE UNIQUE INDEX ux_wallets_single_primary ON wallets.wallets ((is_primary)) WHERE is_primary;

CREATE INDEX ix_wallets_display_order ON wallets.wallets (display_order) WHERE NOT is_archived;
CREATE INDEX ix_wallets_wallet_type ON wallets.wallets (wallet_type_id);
CREATE INDEX ix_wallets_currency ON wallets.wallets (currency_id);
```

`ux_wallets_single_primary` — частичный уникальный индекс на константном условии `is_primary = true`: PostgreSQL позволяет не более одной строки, где это условие истинно, что на уровне БД гарантирует инвариант «ровно один основной кошелек» (в сочетании с обязательностью выбора основного на уровне приложения — БД сама по себе не может заставить *хотя бы один* быть основным, только не более одного).

## Схема `operations`

```sql
CREATE SCHEMA operations;

CREATE TABLE operations.transfers (
    id                   uuid PRIMARY KEY,
    source_wallet_id     uuid        NOT NULL,  -- wallets.wallets.id, без FK
    target_wallet_id     uuid        NOT NULL,
    source_operation_id  uuid        NOT NULL,  -- FK добавляется ниже после создания operations (взаимная ссылка)
    target_operation_id  uuid        NOT NULL,
    amount                numeric     NOT NULL CHECK (amount > 0),
    currency_id           uuid        NOT NULL,  -- reference_data.currencies.id, без FK
    transfer_date          date        NOT NULL,
    deleted_at             timestamptz,  -- NULL = не удален; мягкое удаление (ADR-0002 §3), совпадает с deleted_at обеих «ног»
    created_at             timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_transfers_distinct_wallets CHECK (source_wallet_id <> target_wallet_id)
);

CREATE TABLE operations.operations (
    id                     uuid PRIMARY KEY,
    wallet_id              uuid         NOT NULL,  -- wallets.wallets.id, без FK
    operation_type_id      uuid,        -- reference_data.operation_types.id, без FK; NULL только у корректирующей суммы (is_correction)
    amount                 numeric      NOT NULL,
    applied_delta_amount   numeric      NOT NULL,  -- фактически применённая к балансу кошелька знаковая дельта — см. ниже
    currency_id            uuid         NOT NULL,  -- reference_data.currencies.id, без FK; должна совпадать с currency_id кошелька (проверка в приложении)
    operation_date         date         NOT NULL,
    adjustment_mode        varchar(16),  -- 'Delta' | NULL; режима 'Absolute' больше нет (ADR-0003 §6, п.9)
    transfer_id            uuid,        -- FK добавлена ниже как DEFERRABLE (взаимная ссылка с transfers)
    deleted_at             timestamptz, -- NULL = не удалена; мягкое удаление (ADR-0002 §3): строка остается в БД и в списках
    is_correction          boolean      NOT NULL DEFAULT false,  -- корректирующая сумма заявленного баланса (ADR-0003 §6) — см. «Про корректирующие суммы»
    stated_balance_amount  numeric,     -- заявленный баланс X (факт от пользователя); NOT NULL только у корректирующей суммы
    created_at              timestamptz  NOT NULL DEFAULT now(),
    updated_at              timestamptz  NOT NULL DEFAULT now(),

    CONSTRAINT ck_operations_adjustment_mode CHECK (adjustment_mode IN ('Delta') OR adjustment_mode IS NULL),
    CONSTRAINT ck_operations_correction_stated_balance CHECK (is_correction = (stated_balance_amount IS NOT NULL)),
    CONSTRAINT ck_operations_correction_shape CHECK (
        NOT is_correction
        OR (operation_type_id IS NULL AND adjustment_mode IS NULL AND transfer_id IS NULL AND deleted_at IS NULL)),
    CONSTRAINT ck_operations_regular_type CHECK (is_correction OR operation_type_id IS NOT NULL)
);

-- Циклическая зависимость operations.transfer_id <-> transfers.source/target_operation_id
-- (Transfer и обе его Operation создаются в одной транзакции, ссылаясь друг на друга) —
-- решена через DEFERRABLE INITIALLY DEFERRED: ограничения проверяются только при COMMIT
-- транзакции, поэтому порядок вставки трёх строк внутри неё не имеет значения (не нужен
-- отдельный проход "вставить с NULL -> обновить").
ALTER TABLE operations.operations
    ADD CONSTRAINT fk_operations_transfer FOREIGN KEY (transfer_id) REFERENCES operations.transfers(id)
        DEFERRABLE INITIALLY DEFERRED;

ALTER TABLE operations.transfers
    ADD CONSTRAINT fk_transfers_source_operation FOREIGN KEY (source_operation_id) REFERENCES operations.operations(id)
        DEFERRABLE INITIALLY DEFERRED,
    ADD CONSTRAINT fk_transfers_target_operation FOREIGN KEY (target_operation_id) REFERENCES operations.operations(id)
        DEFERRABLE INITIALLY DEFERRED,
    ADD CONSTRAINT uq_transfers_source_operation UNIQUE (source_operation_id),
    ADD CONSTRAINT uq_transfers_target_operation UNIQUE (target_operation_id);

CREATE INDEX ix_operations_wallet_date ON operations.operations (wallet_id, operation_date);
CREATE INDEX ix_operations_operation_type ON operations.operations (operation_type_id);
CREATE INDEX ix_operations_transfer ON operations.operations (transfer_id) WHERE transfer_id IS NOT NULL;

-- Не более одного заявленного баланса на пару (кошелёк, дата): повторный ввод заменяет значение в той же строке (ADR-0003 §6, п.8)
CREATE UNIQUE INDEX ux_operations_wallet_correction_date ON operations.operations (wallet_id, operation_date) WHERE is_correction;
```

**Про `applied_delta_amount`.** Добавлена при реализации среза Operations (не было в первой версии схемы) — хранит фактически применённую к `wallets.current_balance_amount` знаковую дельту, отдельно от пользовательского ввода (`amount` + `adjustment_mode`). Нужна, чтобы `UpdateOperation`/`DeleteOperation` могли корректно отменить именно тот эффект, который был применён ранее (а `RestoreOperation` — применить его заново). Для обычных операций, включая корректировку дельтой (`adjustment_mode = 'Delta'`), `applied_delta_amount` выводится из `amount` и поведения типа операции; хранится как зафиксированный эффект, чтобы не зависеть от последующих изменений справочника. У корректирующей суммы `amount = applied_delta_amount` — рассчитанная системой сумма со знаком. Режима `Absolute` больше нет: прежняя абсолютная корректировка считала дельту от баланса кошелька в момент ввода и замораживала её — теперь абсолютное значение задаётся заявленным балансом, а его сумма пересчитывается (см. ниже). Подробности — `Operations.Domain.Operation` и `Operations.Application.OperationEffectCalculator` (только `Delta`).

**Про корректирующие суммы.** Заявленный баланс ([ADR-0003](../architecture/adr/0003-balance-history.md), §6) и его корректирующая сумма хранятся одной строкой `operations.operations` с `is_correction = true` ([ADR-0003](../architecture/adr/0003-balance-history.md), §7, п.1; отдельной таблицы нет). `DEFAULT false` у `is_correction` задаёт миграция (значение для уже существующих строк); в модели EF значения по умолчанию нет — приложение всегда записывает признак явно. Колонка `stated_balance_amount` — заявленный баланс X на конец дня `operation_date` (введено пользователем, система не меняет); `amount` = `applied_delta_amount` — сумма со знаком, равная X минус баланс на конец дня `operation_date` без неё (рассчитано системой, пересчитывается при любом изменении операций кошелька с датой ≤ `operation_date`). У корректирующей строки нет типа операции, режима корректировки, перевода и признака удаления — это гарантируют CHECK-ограничения:
- `ck_operations_correction_stated_balance` — `is_correction` ⇔ `stated_balance_amount IS NOT NULL`;
- `ck_operations_correction_shape` — корректирующая строка без `operation_type_id`, `adjustment_mode`, `transfer_id` и `deleted_at` (мягкое удаление ей не подлежит, ADR-0002 §3);
- `ck_operations_regular_type` — у обычной строки `operation_type_id` обязателен (колонка стала nullable только ради корректирующей суммы);
- `ck_operations_adjustment_mode` — `adjustment_mode` ∈ {`Delta`} или NULL.

Уникальный частичный индекс `ux_operations_wallet_correction_date` обеспечивает не более одного заявленного баланса на пару (кошелёк, дата); при гонке двух одновременных вводов нарушение уникальности (`23505`) превращается в 409, обычный путь сериализуется advisory lock кошелька. Корректирующие строки не считаются историей кошелька и использованием справочников (`IWalletHistorySource` и `IReferenceItemUsageProbe` их исключают), входят в порт `IWalletOperationsLookup` как обычные дельты (поэтому слепки, баланс на дату и историческая общая сумма их учитывают), физически удаляются только вместе с кошельком, а `currency_id` следует за валютой кошелька. Нулевая корректирующая строка существует, но из `GET /operations` исключается (`is_correction AND amount = 0`); порядок списка — `(operation_date, is_correction, created_at)` по возрастанию. Новых индексов, кроме `ux_…`, нет.

**Миграция `AddStatedBalanceCorrections`** (Operations; преобразование данных — `migrationBuilder.Sql`) превращает прежние абсолютные корректировки (`adjustment_mode = 'Absolute'`) в заявленные балансы ([ADR-0003](../architecture/adr/0003-balance-history.md), §6; решение пользователя 2026-09-30: существующие данные тестовые, «Итог» п.2). Порядок:
1. Добавляются колонки `is_correction`, `stated_balance_amount`; `operation_type_id` становится nullable.
2. Среди **не удалённых** строк `Absolute` в каждой паре (кошелёк, дата) берётся самая поздняя по `(created_at, id)`: она становится корректирующей — `stated_balance_amount := amount`, `amount := applied_delta_amount` (прежний эффект до сверки), `operation_type_id := NULL`, `adjustment_mode := NULL`, `is_correction := true`.
3. Остальные строки `Absolute` (более ранние дубли на ту же дату и мягко удалённые) становятся обычной корректировкой дельтой: `adjustment_mode := 'Delta'`, `amount := applied_delta_amount`. Их эффект на баланс сохраняется в точности; дубли на дату D поглощаются суммой на D.
4. Создаются CHECK-ограничения и индекс.
5. `Down`: при наличии строк с `is_correction` падает с исключением БД (`RAISE EXCEPTION`: откат невозможен без потери заявленных балансов) и ничего не теряет; иначе удаляет колонки и ограничения, возвращает `operation_type_id NOT NULL` и прежний CHECK режима. Строки, ставшие `Delta`, обратно в `Absolute` не превращаются.

Преобразование теряет только `operation_type_id` преобразованных строк, а также X у дублей и удалённых строк (значения остаются в записях аудита `Created`). После миграции фоновая сверка при старте приложения (`ReconcileWalletCorrections`) пересчитывает суммы от баланса на дату D (суммы `Absolute` считались от текущего баланса кошелька) и приводит `wallets.current_balance_amount` к `initial_balance_amount + Σ applied_delta_amount`. Перед слиянием в `master` проверить боевую базу на такие строки (изменение данных — «Критичный стоп», п.2 `CLAUDE.md`, только если они там есть; в пустых базах стопа нет).

**Про `deleted_at`.** Мягкое удаление операций и переводов ([ADR-0002](../architecture/adr/0002-record-lifecycle.md), §3): одна nullable-колонка одновременно служит признаком удаления (`IS NOT NULL`) и моментом удаления; `NULL` — запись не удалена. Физического удаления строк `operations`/`transfers` нет; «ноги» перевода получают то же значение `deleted_at`, что и сам перевод. Удаленные строки остаются видимыми в списках и учитываются при определении истории кошелька и использования элемента справочника, но исключаются из расчета баланса (слепки, баланс на дату, общая сумма) — фильтр `deleted_at IS NULL` применяется явно в запросах расчета, а не глобально. Значение `updated_at` при удалении и восстановлении не меняется. Новых индексов нет: выборки по-прежнему идут по `ix_operations_wallet_date`. Добавление nullable-колонки не затрагивает существующие строки (все — «не удалены»).

**Про `operations.amount` без CHECK на знак.** Сознательно не ограничен на уровне БД (в отличие от `transfers.amount > 0`): направление влияния на баланс (прибавить/вычесть) для Income/Expense/Transfer определяется через `operation_type_id → behavior_kind_id`, лежащий в другой схеме (`reference_data`) — проверка знака требовала бы либо кросс-схемной ссылки (запрещено ADR-0001), либо триггера с кросс-схемным запросом (хрупко). Для `Adjustment` со значением `Delta` сумма может быть отрицательной по смыслу (уменьшение баланса), поэтому единый CHECK на положительность был бы и технически проблематичным, и семантически неверным. Валидация знака/направления суммы выполняется в Domain-слое модуля `Operations` в момент создания/редактирования операции.

## Схема `balance_history`

```sql
CREATE SCHEMA balance_history;

CREATE TABLE balance_history.balance_snapshots (
    wallet_id       uuid        NOT NULL,  -- wallets.wallets.id, без FK
    snapshot_date   date        NOT NULL,
    balance_amount  numeric     NOT NULL,
    currency_id     uuid        NOT NULL,  -- reference_data.currencies.id, без FK
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (wallet_id, snapshot_date)  -- явное требование раздела 5: не более одной записи на кошелек и дату
);

CREATE INDEX ix_balance_snapshots_wallet_date_desc
    ON balance_history.balance_snapshots (wallet_id, snapshot_date DESC);
```

Составной первичный ключ `(wallet_id, snapshot_date)` напрямую отражает идентичность агрегата `BalanceSnapshot` из DDD-модели — отдельного суррогатного `id` не заводится, он не нужен: снепшот нигде не адресуется по отдельному Id, только по паре.

## Схема `exchange_rates`

```sql
CREATE SCHEMA exchange_rates;

CREATE TABLE exchange_rates.latest_exchange_rates (
    from_currency_id  uuid        NOT NULL,  -- reference_data.currencies.id, без FK
    to_currency_id    uuid        NOT NULL,
    rate               numeric     NOT NULL CHECK (rate > 0),
    fetched_at         timestamptz NOT NULL,  -- «дата и время последнего успешного обновления» (раздел 2)

    PRIMARY KEY (from_currency_id, to_currency_id),
    CONSTRAINT ck_latest_rates_distinct_currencies CHECK (from_currency_id <> to_currency_id)
);

CREATE TABLE exchange_rates.historical_exchange_rates (
    from_currency_id  uuid        NOT NULL,
    to_currency_id    uuid        NOT NULL,
    rate_date          date        NOT NULL,
    rate                numeric     NOT NULL CHECK (rate > 0),
    fetched_at           timestamptz NOT NULL,

    PRIMARY KEY (from_currency_id, to_currency_id, rate_date),
    CONSTRAINT ck_historical_rates_distinct_currencies CHECK (from_currency_id <> to_currency_id)
);

-- Какой диапазон дат истории уже загружен для пары (ADR-0004, §9). Одна строка на пару.
CREATE TABLE exchange_rates.historical_rate_coverage (
    from_currency_id  uuid        NOT NULL,  -- reference_data.currencies.id, без FK
    to_currency_id    uuid        NOT NULL,
    covered_from      date        NOT NULL,  -- левая граница загруженного диапазона
    covered_to        date        NOT NULL,  -- правая граница; продлевается до сегодняшнего дня
    updated_at        timestamptz NOT NULL,

    PRIMARY KEY (from_currency_id, to_currency_id),
    CONSTRAINT ck_historical_coverage_distinct_currencies CHECK (from_currency_id <> to_currency_id),
    CONSTRAINT ck_historical_coverage_range CHECK (covered_from <= covered_to)
);
```

Две отдельные таблицы курсов, а не одна с nullable-датой: разный жизненный цикл. `latest_exchange_rates` — upsert одной строки на пару при каждом обновлении. `historical_exchange_rates` — **загружаемая история**, а не накопление по требованию: ее заполняет фоновая дозагрузка (ADR-0004, §9) для активных валют к основной за период от самой ранней даты начала учета (по всем кошелькам, включая архивные, с запасом в несколько дней слева) до сегодняшнего дня; строки записываются upsert'ом по `(from_currency_id, to_currency_id, rate_date)` — повторная загрузка того же дня обновляет курс (таблица не append-only). Чтение курсов (`GET /reporting/total-amount`) идет только из этой таблицы: курс на дату или ближайший более ранний; к сервису при чтении обращений нет (§8). Строки для дат без публикации курса (выходные) не создаются.

`historical_rate_coverage` делает дозагрузку идемпотентной и возобновляемой: диапазон `[covered_from; covered_to]` непрерывен, поэтому прерванный проход продолжается с места остановки, а повторный запрашивает только недостающие края; заведомо пустой ранний период (у сервиса нет данных раньше некоторой даты) фиксируется сдвигом `covered_from` и больше не перезапрашивается. Покрытие и история пар не к текущей основной валюте или с неактивной валютой удаляются в конце прохода дозагрузки; при удалении валюты (`CurrencyDeleted`) — в той же транзакции, вместе с курсами пары.

При смене основной валюты (смена основного кошелька или валюты основного кошелька) история и покрытие пар к прежней основной валюте удаляются, пары к новой загружаются за весь период заново: пока дозагрузка не завершена, исторические курсы за часть дат отсутствуют, и кошельки без курса исключаются из исторической общей суммы.

## Схема `audit`

```sql
CREATE SCHEMA audit;

CREATE TABLE audit.audit_entries (
    id                    uuid        PRIMARY KEY,
    entity_type           varchar(64) NOT NULL,   -- 'Wallet' | 'Operation' | 'Transfer' | 'BalanceSnapshot' | 'ExchangeRateQuote' | 'WalletType' | 'OperationType' | 'Currency'
    entity_id             uuid        NOT NULL,
    action                 varchar(32) NOT NULL,   -- 'Created' | 'Updated' | 'Deleted' | 'Restored' | 'Archived' | 'Deactivated' | 'SnapshotCreated' | 'SnapshotUpdated' | 'SnapshotDeleted' | 'RatesUpdated' | 'RateUpdateFailed' | 'HistoryLoaded' | ...
    occurred_at             timestamptz NOT NULL DEFAULT now(),
    actor_kind               varchar(16) NOT NULL CHECK (actor_kind IN ('User', 'System')),
    actor_system_process     varchar(128),
    changes                   jsonb       NOT NULL DEFAULT '[]'::jsonb  -- [{ "field": "...", "oldValue": "...", "newValue": "..." }, ...]
);

CREATE INDEX ix_audit_entries_entity ON audit.audit_entries (entity_type, entity_id, occurred_at DESC);

-- Неизменяемость журнала (раздел 6: «история аудита не должна теряться») — защита на уровне БД в дополнение к слою приложения.
CREATE RULE audit_entries_no_update AS ON UPDATE TO audit.audit_entries DO INSTEAD NOTHING;
CREATE RULE audit_entries_no_delete AS ON DELETE TO audit.audit_entries DO INSTEAD NOTHING;
```

**Про `action` без справочной таблицы.** В отличие от `OperationBehaviorKind` (который пользователь явно видит и с которым связывает свои типы операций), `action` — чисто техническая классификация внутри модуля Audit, никогда не выбирается пользователем и не расширяется без изменения кода ни одного из модулей-издателей событий. Поэтому здесь достаточно `varchar` без CHECK на конкретный список значений (список расширяется при добавлении новых доменных событий без миграции БД) — в отличие от `wallet_types`/`operation_types`/`currencies`, это не пользовательский справочник.

**Про `changes jsonb`.** Список измененных полей (VO `AuditFieldChange` из `ddd-model.md`) хранится как JSONB-массив, а не в отдельной нормализованной таблице — так как список полей разный для каждого `entity_type` и запросы вида «найти все изменения конкретного поля» не входят в бизнес-сценарии (UC-25 — только просмотр истории конкретной записи целиком). Если такая потребность появится, это будет пересмотрено отдельно.

## Сводная таблица: схема ↔ модуль ↔ таблицы

| Схема | Модуль | Таблицы |
|---|---|---|
| `reference_data` | ReferenceData | `operation_behavior_kinds`, `wallet_types`, `operation_types`, `currencies`, `available_currencies` |
| `wallets` | Wallets | `wallets` |
| `operations` | Operations | `operations`, `transfers` |
| `balance_history` | BalanceHistory | `balance_snapshots` |
| `exchange_rates` | ExchangeRates | `latest_exchange_rates`, `historical_exchange_rates`, `historical_rate_coverage` |
| `audit` | Audit | `audit_entries` |

`Reporting` не владеет таблицами — читает через узкие read-контракты остальных модулей или (как будущая оптимизация) через SQL-представления, объединяющие несколько схем на чтение.
