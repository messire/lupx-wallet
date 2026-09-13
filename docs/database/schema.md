# Схема базы данных (PostgreSQL)

Источники: [ddd-model.md](../architecture/ddd-model.md), [high-level-architecture.md](../architecture/high-level-architecture.md), ADR-0001…ADR-0006.

Технические идентификаторы (схемы, таблицы, колонки) — на английском (`snake_case`, идиоматично для PostgreSQL); пояснения — на русском.

## Общие соглашения

- **Схема на модуль**, согласно ADR-0006: `wallets`, `reference_data`, `operations`, `balance_history`, `exchange_rates`, `audit`. `Reporting` собственной схемы не имеет (только читает).
- **Без внешних ключей между схемами разных модулей.** Ссылка на сущность другого модуля хранится как обычная колонка `uuid` (например, `operations.wallet_id`), без `REFERENCES` на чужую схему — ссылочная целостность обеспечивается на уровне приложения (ADR-0006). Внешние ключи **внутри одной схемы** (например, `operation_types.behavior_kind_id → reference_data.operation_behavior_kinds.id`) используются как обычно.
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

CREATE TABLE reference_data.currencies (
    id          uuid PRIMARY KEY,
    code        varchar(16)  NOT NULL,   -- напр. 'USD', 'BTC' — свободный код, не обязательно ISO 4217
    name        varchar(200) NOT NULL,
    is_active   boolean      NOT NULL DEFAULT true,
    created_at  timestamptz  NOT NULL DEFAULT now(),
    updated_at  timestamptz  NOT NULL DEFAULT now()
);
-- Код валюты уникален глобально (включая неактивные) — переиспользование кода для другой валюты недопустимо, это исказило бы историю.
CREATE UNIQUE INDEX ux_currencies_code ON reference_data.currencies (upper(code));
```

## Схема `wallets`

```sql
CREATE SCHEMA wallets;

CREATE TABLE wallets.wallets (
    id                      uuid PRIMARY KEY,
    name                    varchar(200) NOT NULL,
    wallet_type_id          uuid         NOT NULL,   -- reference_data.wallet_types.id, без FK (ADR-0006)
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
    created_at             timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_transfers_distinct_wallets CHECK (source_wallet_id <> target_wallet_id)
);

CREATE TABLE operations.operations (
    id                     uuid PRIMARY KEY,
    wallet_id              uuid         NOT NULL,  -- wallets.wallets.id, без FK
    operation_type_id      uuid         NOT NULL,  -- reference_data.operation_types.id, без FK
    amount                 numeric      NOT NULL,
    applied_delta_amount   numeric      NOT NULL,  -- фактически применённая к балансу кошелька знаковая дельта — см. ниже
    currency_id            uuid         NOT NULL,  -- reference_data.currencies.id, без FK; должна совпадать с currency_id кошелька (проверка в приложении)
    operation_date         date         NOT NULL,
    adjustment_mode        varchar(16)  CHECK (adjustment_mode IN ('Absolute', 'Delta') OR adjustment_mode IS NULL),
    transfer_id            uuid,        -- FK добавлена ниже как DEFERRABLE (взаимная ссылка с transfers)
    created_at              timestamptz  NOT NULL DEFAULT now(),
    updated_at              timestamptz  NOT NULL DEFAULT now()
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
```

**Про `applied_delta_amount`.** Добавлена при реализации среза Operations (не было в первой версии схемы) — хранит фактически применённую к `wallets.current_balance_amount` знаковую дельту, отдельно от пользовательского ввода (`amount` + `adjustment_mode`). Нужна, чтобы `UpdateOperation`/`DeleteOperation` могли корректно отменить именно тот эффект, который был применён ранее — особенно для `Adjustment`/`Absolute`, где применённая дельта зависела от баланса кошелька в момент применения и невыводима заново из одного `amount`. Подробности — `Operations.Domain.Operation` и `Operations.Application.OperationEffectCalculator`.

**Про `operations.amount` без CHECK на знак.** Сознательно не ограничен на уровне БД (в отличие от `transfers.amount > 0`): направление влияния на баланс (прибавить/вычесть) для Income/Expense/Transfer определяется через `operation_type_id → behavior_kind_id`, лежащий в другой схеме (`reference_data`) — проверка знака требовала бы либо кросс-схемной ссылки (запрещено ADR-0006), либо триггера с кросс-схемным запросом (хрупко). Для `Adjustment` со значением `Delta` сумма может быть отрицательной по смыслу (уменьшение баланса), поэтому единый CHECK на положительность был бы и технически проблематичным, и семантически неверным. Валидация знака/направления суммы выполняется в Domain-слое модуля `Operations` в момент создания/редактирования операции.

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
```

Две отдельные таблицы, а не одна с nullable-датой: разный жизненный цикл (`latest_exchange_rates` — upsert одной строки на пару при каждом обновлении; `historical_exchange_rates` — append-only, пополняется по мере того, как приложению реально требуется курс на конкретную прошедшую дату для расчета исторической общей суммы, раздел 5).

## Схема `audit`

```sql
CREATE SCHEMA audit;

CREATE TABLE audit.audit_entries (
    id                    uuid        PRIMARY KEY,
    entity_type           varchar(64) NOT NULL,   -- 'Wallet' | 'Operation' | 'Transfer' | 'BalanceSnapshot' | 'ExchangeRateQuote' | 'WalletType' | 'OperationType' | 'Currency'
    entity_id             uuid        NOT NULL,
    action                 varchar(32) NOT NULL,   -- 'Created' | 'Updated' | 'Deleted' | 'Archived' | 'Deactivated' | 'SnapshotCreated' | 'SnapshotUpdated' | 'RatesUpdated' | 'RateUpdateFailed' | ...
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
| `reference_data` | ReferenceData | `operation_behavior_kinds`, `wallet_types`, `operation_types`, `currencies` |
| `wallets` | Wallets | `wallets` |
| `operations` | Operations | `operations`, `transfers` |
| `balance_history` | BalanceHistory | `balance_snapshots` |
| `exchange_rates` | ExchangeRates | `latest_exchange_rates`, `historical_exchange_rates` |
| `audit` | Audit | `audit_entries` |

`Reporting` не владеет таблицами — читает через узкие read-контракты остальных модулей или (как будущая оптимизация) через SQL-представления, объединяющие несколько схем на чтение.
