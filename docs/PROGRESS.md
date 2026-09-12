# Прогресс проекта

Последнее обновление: 2026-09-12 — структурный рефакторинг по ADR-0012 завершён (backend перенесён в `backend/`, единый шаблон модуля применён ко всем 7 модулям, frontend разделён на `data-access`/`features`/`shared`, добавлена проверка границ features). Исторические записи ниже сохраняют статусы на момент соответствующих этапов.

## Пройденные этапы

| Этап | Результат | Коммит |
|---|---|---|
| Бизнес-требования | Проверены на противоречия, все открытые вопросы (Q1–Q16) закрыты | `ed3b967` |
| Пользовательские сценарии и глоссарий | [user-scenarios.md](requirements/user-scenarios.md), [glossary.md](requirements/glossary.md) | `ed3b967` |
| DDD-модель | [ddd-model.md](architecture/ddd-model.md) — 7 bounded context'ов, 8 агрегатов, VO, инварианты, события | `a566634` |
| Высокоуровневая архитектура | [high-level-architecture.md](architecture/high-level-architecture.md), ADR-0001…ADR-0006 | `a566634` |
| Схема БД | [schema.md](database/schema.md) — 6 схем PostgreSQL | `a566634` |
| API-контракты | [openapi.yaml](api/openapi.yaml) (валиден, 0 ошибок redocly lint), [api-design.md](api/api-design.md) | `a566634` |
| **Срез: Auth + скелет решения** | Скелет .NET-solution (30 проектов, 7 модулей + SharedKernel + BuildingBlocks.Infrastructure + Host), полный цикл логина по единому паролю (JWT, PBKDF2, rate limiting), Angular-приложение с guard/interceptor | `a566634` |
| **Срез: Wallets** | Агрегат `Wallet`, `PrimaryWalletPolicy`, EF Core миграция схемы `wallets`, `POST/GET /api/v1/wallets` с курсорной пагинацией, экран списка + формы создания в Angular. Проверено вживую на реальном PostgreSQL | `607fb60` |
| **Срез: ReferenceData** | Агрегаты `WalletType`, `OperationType`, `Currency` (create/deactivate/delete + список), системный `OperationBehaviorKind` (4 строки, seed через миграцию). EF Core миграция схемы `reference_data`. Форма создания кошелька в Angular переведена с ручного ввода UUID на выпадающие списки + мини-формы быстрого добавления. Проверено вживую | `8ad1ee2` |
| **Срез: Operations (+ Transfers)** | Агрегаты `Operation` (Income/Expense/Adjustment, оба режима корректировки) и `Transfer` (атомарная пара операций). Два новых межмодульных контракта (ADR-0007): `IOperationTypeLookup` (ReferenceData→Operations) и `IWalletBalanceGateway` (Wallets→Operations) — операция реально меняет `CurrentBalance` кошелька синхронно в общей транзакции. EF Core миграция схемы `operations` с `DEFERRABLE`-констрейнтами (циклическая FK operations.transfer_id ↔ transfers.source/target_operation_id). CRUD для операций и переводов проверены вживую сквозным сценарием (доход→расход→обе корректировки→изменение→удаление, с точной сверкой баланса на каждом шаге) и переводом (создание/защита от прямого изменения операций перевода/удаление). Найдены и исправлены 2 реальных бага при тестировании (см. ниже). **Angular UI для операций/переводов не реализован.** | `3d79b30` |
| **Срез: BalanceHistory (бэкенд)** | Агрегат `BalanceSnapshot` (пара WalletId+SnapshotDate — без суррогатного Id, ADR-0003 §2.6). Единый `BalanceRecalculationService` (ddd-model.md §2.6), переиспользуемый двумя триггерами: каскадный пересчет по событиям Operations (ADR-0003) и плановый прогон в 12:00 UTC/при старте (ADR-0004, объединены в одном самовосстанавливающемся `BackgroundService` — см. ниже). **ADR-0008**: BalanceHistory — первый реальный подписчик на доменные события (ADR-0007 относил его к необязательным реакциям); потребовал переноса диспетчеризации `DispatchDomainEventsInterceptor` с pre-save на post-save (иначе подписчик, читающий Operations через собственный DbContext в том же scope, не видел бы только что сохраненные данные) и обогащения событий Operations датой операции. Новые межмодульные контракты: `Operations.Application.IWalletOperationsLookup`, `Wallets.Application.IWalletDirectory`, `IWalletBalanceGateway` расширен (InitialBalance/AccountingStartDate). `GET /wallets/{id}/balance` и `/balance-history` — проверены вживую: создание/изменение/удаление операции и перевода задним числом корректно каскадно пересчитывает диапазон [дата..сегодня] на обоих кошельках перевода, баланс "сегодня" совпадает с `Wallet.CurrentBalance` на каждом шаге; курсорная пагинация; ошибки валидации — вживую. Ревью (code-quality-reviewer) в два прохода нашло и все находки устранены — см. раздел "Баги, найденные и исправленные" ниже. **Angular UI не реализован.** | *(следующий коммит)* |
| **Срез: ExchangeRates + Reporting + Audit + завершение Wallets** | Три новых backend-модуля (ExchangeRates — интеграция с Frankfurter, ADR-0001/ADR-0011; Reporting — Total Amount текущая/историческая, решение по риску №6 исключённых кошельков; Audit — сквозной подписчик на события всех модулей, ADR-0009/ADR-0010/ADR-0011), завершён жизненный цикл Wallets (UC-02…UC-06), весь оставшийся Angular UI (Operations/Transfers, справочники, BalanceHistory, ExchangeRates, Reporting, Audit, действия над кошельком), закрыты кросс-модульные TODO по ADR-0009 (случаи a/c реализованы, случай b — закрыт без порта, решение пользователя). Код-ревью и UX-ревью проведены, находки устранены. Подробности — см. раздел «ПЛАН: доведение приложения до полного соответствия архитектуре и требованиям» и записи по волнам ниже | `775f951` |
| **Структурный рефакторинг (ADR-0012)** | Backend перенесён `src/`→`backend/src/`, `tests/`→`backend/tests/`; единый шаблон модуля (`Domain/Application/Infrastructure/Api`, короткие имена папок, `UseCases/Abstractions/Persistence/Models/Policies` и т.д.) применён ко всем 7 модулям + `BuildingBlocks`. Frontend: `data-access/<domain>` отделён от `features/<domain>/{pages,components}`, автоматическая проверка границ features (`check:boundaries`). Doc-комментарии переведены на английский и сжаты. 30 backend-проектов и вся бизнес-логика/контракты/схема БД сохранены без изменений — подтверждено 188 unit + 83 integration + 114 frontend тестами. Подробности — см. раздел «ПЛАН: структурный рефакторинг» ниже | *(следующий коммит)* |

## Следующий шаг

Структурный рефакторинг по ADR-0012 завершён (см. план ниже) — backend в `backend/`, frontend разделён на `data-access`/`features`/`shared`, конвенции задокументированы. Следующий шаг не определён — ждёт запроса пользователя (например: настройка деплоя Vercel/Railway, или продолжение бизнес-функциональности).

## ПЛАН: структурный рефакторинг (2026-09-12)

**Статус:** ADR-0012 принят, выполнение начато. Цель — предсказуемое расположение файлов и единые конвенции без изменения поведения приложения.

Целевые деревья репозитория, backend-модуля и frontend, правила размещения и компромиссы: [ADR-0012](architecture/adr/0012-repository-structure-and-conventions.md), статус «Принято». Дополнительно к исходному предложению зафиксировано решение пользователя (2026-09-12): doc-комментарии в коде (XML `<summary>`/`<param>` в C#, JSDoc в TypeScript) — только на английском и сжатые (что делает + краткое назначение параметров, без истории изменений и очевидных пояснений). План и статусы ведутся здесь; отдельный журнал планирования не создаётся.

- [x] Сохранить предложение в ADR-0012 и план в документации.
- [x] Уточнить и принять ADR (2026-09-12) — добавлены правила doc-комментариев (английский язык, сжатый формат).
- [x] Оформить короткие правила в `docs/conventions/repository-structure.md`, `backend.md`, `frontend.md` (включая правило doc-комментариев).
- [x] Зафиксировать исходные результаты сборок и существующих тестов перед переносами (2026-09-12): backend `dotnet build` — 0 ошибок/2 pre-existing warning; `dotnet test tests/LupexWallet.UnitTests` — 188/188; frontend `ng build` — успешно; `ng test` — 114/114 (14 файлов). Интеграционные тесты (Testcontainers/Docker) в baseline не запускались — не меняются переносом путей, проект компилируется в общей сборке.
- [x] Перенести `src/`, `tests/`, solution и общие .NET-настройки в `backend/` (2026-09-12, `git mv`, без правок содержимого — все относительные `ProjectReference` и .sln-пути не изменились, т.к. перенесены как единое поддерево). Обновлены пути в `README.md` (сокращён до точки входа), создан `backend/README.md`, обновлены `docs/architecture/high-level-architecture.md` и `CLAUDE.md`. CI не существует (нечего обновлять). `docker-compose.yml` не ссылался на `src/`/`tests/` — не требовал правок, остался в корне. Проверено: backend build/test после переноса — идентичный результат (188/188).
- [x] Привести Wallets к образцу: короткие названия папок слоёв, `UseCases`, `Abstractions`, `Models`, `Policies`, `Persistence`, `Integration`, `Contracts`, `Endpoints`. Имена сборок сохранены.
- [x] Применить шаблон к остальным модулям и BuildingBlocks (2026-09-12, по фактическому графу `ProjectReference`: `ReferenceData → Operations → ExchangeRates → BalanceHistory → {Reporting, Audit}` параллельно — независимые модули запускались параллельно только там, где не пересекались ссылающиеся `.csproj`, иначе последовательно во избежание гонки правок одного файла двумя агентами). `BuildingBlocks.Infrastructure/` → `BuildingBlocks/Infrastructure/` выполнено отдельным шагом (9 ссылающихся `.csproj` + `.sln`). Фоновые задачи — в `Infrastructure/BackgroundJobs/`, внешние клиенты (Frankfurter) — в `Infrastructure/ExternalServices/`. Пустые слои/папки не создавались (`Reporting` — без `Domain`, как и раньше).
- [x] По ходу переноса файлов doc-комментарии (XML `///`) переведены на английский и сжаты во всех затронутых файлах; inline `//`-комментарии и текст бизнес-исключений не трогали, как и договаривались.
- [x] Frontend HTTP-клиенты и модели API вынесены в `data-access/<domain>/` (`<domain>-api.service.ts`/`<domain>-api.models.ts`, сервисы переименованы `XService` → `XApiService`); модели состояния экрана остались в features (пример: `operations.models.ts` разделён — API-контракты в `data-access/operations/operations-api.models.ts`, локальный `OperationTypeOption` остался в feature).
- [x] Страницы и компоненты размещены в `features/<domain>/pages/<domain>-page/` и `features/<domain>/components/<name>/`; тесты/шаблоны/стили — рядом. `core/api/cursor-page.*` → `shared/pagination/`, `core/money/money.*` → `shared/money/`.
- [x] Автоматическая проверка границ: `frontend/scripts/check-feature-boundaries.mjs` (`npm run check:boundaries`) — падает, если файл в `features/<domain>` импортирует что-то из другого `features/<other-domain>` напрямую; на момент написания — 0 нарушений. Backend-правило «`Domain` не зависит от `Infrastructure`» отдельной проверки не потребовало — оно и так enforced на уровне компиляции: `Domain.csproj` физически не имеет `ProjectReference` на `Infrastructure`/EF Core, добавление такой ссылки сломает сборку.
- [x] README обновлены: корневой `README.md` сокращён до точки входа со ссылками, созданы `backend/README.md` (перенесены команды запуска/тестов/auth) и подтверждён существующий `frontend/README.md`. Обновлены пути в `docs/architecture/high-level-architecture.md` и `CLAUDE.md`. Исторические записи в этом файле не переписывались.
- [x] Финальная проверка (2026-09-12): backend `dotnet build LupexWallet.sln` — 0 ошибок/0 warning; `dotnet test tests/LupexWallet.UnitTests` — 188/188; `dotnet test tests/LupexWallet.IntegrationTests` (реальный Postgres, Testcontainers, Docker) — **83/83**, миграции всех 7 модулей обнаружены и применились на одноразовой БД; frontend `ng build` — успешно, размеры lazy-чанков не изменились; `ng test` — 114/114 (14 файлов, совпадает с baseline). Итог: расположение файлов соответствует ADR-0012, поведение приложения не изменено.

**Отклонение от плана (зафиксировано, не скрыто):** при переносе модуля Operations один файл (`OperationQueries.cs`/`TransferQueries.cs`, несколько запросов в одном файле) был разбит на отдельные файлы по одному use case — это выходит за границы этапа («разделение крупных файлов — отдельный этап», см. «Ограничения» ниже). Остальные модули (ReferenceData, ExchangeRates, BalanceHistory, Reporting, Audit) перенесены без разбиения файлов, как и договаривались. Логика не менялась, build/test зелёные на каждом шаге — решение принято оставить как есть, а не откатывать.

**Ограничения:** не объединять 30 backend-проектов (сохранено — 30 проектов), не менять бизнес-логику, HTTP-контракты, схему БД, транзакции, авторизацию, планировщики и UI-поведение (сохранено, подтверждено 83 integration-тестами). Разделение крупных файлов — не в рамках этапа (нарушено один раз, см. «Отклонение» выше), переработка реализации и массовая редактура комментариев вне переносимых файлов — последующие этапы. Настройка Vercel/Railway и публикация приложения не входят в этот план.

**Критерий завершения:** расположение файлов соответствует конвенциям; сборки и тесты проходят; команды и документация актуальны; API, схема БД и поведение сохранены. **Этап завершён (2026-09-12).**

## Баги, найденные и исправленные при тестировании (для истории)

- `DispatchDomainEventsInterceptor` собирал события после `SaveChanges` вместо до — терял бы события удаляемых агрегатов (найдено при проектировании, не при рантайм-тесте).
- EF Core не транслирует `w.Id.Value` в LINQ для типизированных Id — курсорная пагинация Wallets/ReferenceData/Operations использует `(поле, CreatedAt)` вместо Id.
- **Operations**: `GET /wallets/{id}` отсутствовал (пропущен в срезе Wallets) — обнаружено и добавлено при тестировании Operations.
- **Operations**: `OperationEffectCalculator` падал обычным `InvalidOperationException` (→ 500) при попытке создать/изменить операцию с поведением `Transfer` напрямую через `/operations` — теперь бросает доменное исключение (→ 400/409). Валидация режима корректировки туда же перенесена, чтобы она срабатывала до похода в БД.
- **BalanceHistory (ревью code-quality-reviewer, найдено при реализации, устранено в этой же сессии)**:
  - Гонка при конкурентном пересчете одного кошелька (ретроактивная правка + плановый прогон одновременно) могла упасть с нарушением первичного ключа `balance_snapshots` и откатить корректную команду пользователя — добавлена сериализация через `pg_advisory_xact_lock` (`IBalanceHistoryUnitOfWork.AcquireWalletLockAsync`).
  - Необработанное исключение в `BalanceSnapshotSchedulerHostedService` (например, недоступная БД при старте или в момент 12:00 UTC) роняло весь хост — обернуто в try/catch на уровне прохода; заодно ежедневный прогон объединен с логикой досоздания пропусков (был не самовосстанавливающимся при сбое).
  - `DispatchDomainEventsInterceptor` мог потерять/затереть буфер доменных событий при повторном или неуспешном `SaveChangesAsync` на одном экземпляре `DbContext` (реальный сценарий — `IWalletBalanceGateway.ApplyDeltaAsync` вызывает `SaveChangesAsync` дважды за одну команду перевода) — добавлен `SaveChangesFailedAsync` и накопление вместо замены буфера; синхронный `SaveChanges` теперь явно бросает исключение вместо тихой потери событий.
  - `Operations` не валидировал `OperationDate`/`TransferDate` относительно `Wallet.AccountingStartDate`/сегодняшней даты — операция вне диапазона меняла `CurrentBalance`, но некорректно отражалась в истории баланса. Добавлена валидация (`OperationDateValidator`, новое исключение `OperationDateOutOfRangeException` → 400) в `CreateOperationCommand`/`UpdateOperationCommand`/`CreateTransferCommand`, использующая уже существующие границы (`AccountingStartDate` и "сегодня", те же, что определяют материализацию `BalanceSnapshot`, Q13).
  - Денежные суммы в JSON-ответах (`MoneyResponse.Amount`) форматировались через `CurrentCulture` (`ToString("G29")` без `CultureInfo`) в Wallets/Operations/BalanceHistory API — на хосте с другой локалью (например, с запятой как десятичным разделителем) контракт `Money.amount` (строка с точкой по OpenAPI) был бы нарушен. Исправлено на всех 5 местах на `CultureInfo.InvariantCulture`.
  - Курсор пагинации `/balance-history` упрощен: `SnapshotDate` уже часть первичного ключа `(wallet_id, snapshot_date)`, лишний `CreatedAt`-tiebreaker (скопированный по аналогии с `WalletCursor`/`OperationCursor`, где он необходим) был мертвым кодом.
  - `GET /wallets/{id}/balance-history` не проверял `from > to` (тихо возвращал бы пустую страницу) — теперь 400.

## Известные упрощения / TODO

- ~~Кросс-модульная валидация ссылок~~ — закрыто W2.5 (ADR-0009, случаи c и d): `Wallets` проверяет существование/активность `walletTypeId`/`currencyId` через `ICurrencyLookup`/`IWalletTypeLookup`; `ReferenceData` при удалении элемента справочника реально проверяет использование через `IEnumerable<IReferenceItemUsageProbe>` (`WalletsReferenceItemUsageProbe`/`OperationsReferenceItemUsageProbe`/`ExchangeRatesReferenceItemUsageProbe`).
- **Аудит** — модуль-заглушка (`AddAuditModule`/`MapAuditEndpoints` пустые); доменные события всех остальных модулей публикуются, но подписчика пока нет. BalanceHistory теперь реальный подписчик на события Operations (ADR-0008) — Audit будет использовать тот же механизм post-save диспетчеризации.
- ~~Точная граница "связанности" при удалении Operation/Transfer~~ — закрыто W2.5 (ADR-0009, случай b), решение пользователя от 2026-09-11: буквальная проверка "дата ≤ дате последнего слепка" оказалась непригодной (каскадный пересчет ADR-0003/0008 материализует слепок на дату самой операции немедленно) и заменена на "дата операции/перевода строго раньше сегодня" — проверяется локально в `Operations.Application`, без обращения к `BalanceHistory` (см. П.5.2 ниже и ADR-0009).
- **Absolute-корректировка и будущий пересчёт истории** (см. ADR-0003, `OperationEffectCalculator`) — по-прежнему считает baseline от `Wallet.CurrentBalance`, не от полной пересчитанной истории на дату корректировки; это отдельное изменение поведения Operations (влияет на то, как ретроактивные правки более ранних операций взаимодействуют с уже существующей Absolute-корректировкой), не внесено молча вместе с BalanceHistory — требует явного запроса/обсуждения перед реализацией (см. ADR-0008, "Последствия").
- **Управление справочниками (деактивация/удаление) в Angular** — есть только создание (список + мини-форма добавления в экране кошельков); отдельного экрана управления типами/валютами с деактивацией/удалением пока нет.
- **Angular UI для Operations/Transfers/BalanceHistory** — не реализован (см. "Следующий шаг" выше); backend полностью готов и проверен через API (Operations — через API, BalanceHistory — вживую: каскадный пересчет при создании/изменении/удалении операции и перевода, плановая задача и досоздание пропусков при старте).
- **`IWalletOperationsLookup.GetDailyDeltasUpToAsync`** группирует и суммирует дельты операций в памяти (C#), а не через LINQ GroupBy+Sum в БД — сознательный выбор в пользу надежности трансляции (см. комментарий в `WalletOperationsLookup.cs`); при росте числа операций на кошелек может потребовать пересмотра.

## Локальное окружение для разработки

- Backend: `dotnet run` в `src/Host/LupexWallet.Api` (по умолчанию порт зависит от `--urls`/launchSettings; в текущей сессии тестировалось на `:5199`).
- Frontend: `npm start` в `frontend/` (Angular dev server, порт 4200).
- PostgreSQL: Docker-контейнер `lupex-wallet-postgres` (`postgres:16`, БД `lupex_wallet`, пользователь/пароль `postgres`/`postgres`, порт 5432) — поднят вручную для тестирования, не входит в `docker-compose`/CI, его нужно будет формализовать (см. TODO ниже).
- Пароль для входа в приложение при локальной разработке: `ChangeMe123!` (хеш в `appsettings.Development.json`, не для production).
- Строка подключения к БД для локальной разработки — в `appsettings.Development.json` (`ConnectionStrings:LupexWallet`); в production обязательна переменная окружения `ConnectionStrings__LupexWallet` (приложение падает при старте, если её нет).

## Инфраструктурный TODO (не забыть)

- ~~Формализовать поднятие PostgreSQL для разработки~~ — сделано: `docker-compose.yml` в корне репозитория (`docker compose up -d`).
- CI/CD пока не настроен — при появлении первого реального деплоя потребуется отдельный этап.

## Тестовая инфраструктура (Фаза 0 плана автономного конвейера)

До этого среза в решении не было ни одного автотеста — каждый срез проверялся вручную вживую (см. записи выше). Это было главным блокером для автономной работы конвейера суб-агентов (`CLAUDE.md`, раздел «РЕЖИМ ОРКЕСТРАЦИИ»): без автотестов агент не может сам проверить свою работу.

Добавлено:
- `tests/LupexWallet.UnitTests` — xUnit, без внешних зависимостей (домен/application всех реализованных модулей). Смоук-покрытие `Wallet` (создание, инварианты, `ApplyBalanceDelta`).
- `tests/LupexWallet.IntegrationTests` — xUnit + Testcontainers (реальный Postgres 16 в одноразовом контейнере, не зависит от `docker compose`) + `WebApplicationFactory<Program>` (реальный Host) + Respawn (сброс данных между тестами). Один контейнер и один Host на весь прогон тестов (`ApiTestFixture`, collection fixture), а не на каждый тест. Смоук-покрытие аутентификации (`/api/v1/auth/login`, защита эндпоинтов без токена).
- `Directory.Build.props`/`Directory.Packages.props` перенесены из `src/` в корень репозитория, чтобы центральное управление версиями пакетов (CPM) распространялось и на `tests/`.
- `docker-compose.yml` — Postgres для локальной разработки (не используется тестами — у них свой контейнер).

Найден и исправлен при подключении инфраструктуры реальный баг в паттерне тестирования (не в проде): `Add<Module>Module(...)` в каждом модуле читает `IConfiguration.GetConnectionString("LupexWallet")` один раз при регистрации `DbContext` и замыкает строку в лямбду — переопределение конфигурации через `WebApplicationFactory.ConfigureWebHost`/`ConfigureAppConfiguration` (стандартный способ подмены конфигурации в интеграционных тестах) применяется позже и не успевает подействовать. Решение — выставлять `ConnectionStrings__LupexWallet` через переменную окружения до первого обращения к `WebApplicationFactory.Services` (в конструкторе `LupexWalletApiFactory`, а не в `ConfigureWebHost`).

Заодно найден и исправлен нерабочий дефолтный тест Angular-скелета (`frontend/src/app/app.spec.ts` проверял текст `<h1>Hello, lupex-wallet-web</h1>`, которого нет в реальном приложении — заменено на проверку `<router-outlet>`).

### Ретроактивное покрытие бизнес-логики (tdd-test-engineer)

Покрыты Wallets, ReferenceData, Operations, BalanceHistory — весь код, реализованный до этого среза и ранее проверенный только вживую (см. таблицу "Пройденные этапы" и раздел "Баги, найденные и исправленные" выше). `dotnet test` на оба проекта — зелёный (103 unit + 39 integration тестов, `dotnet test tests/LupexWallet.UnitTests/...` и `tests/LupexWallet.IntegrationTests/...`, интеграционные требуют запущенный Docker).

### Покрытие каркаса фронтенда (W0.2, tdd-test-engineer)

Добавлены unit-спеки на чистую логику `frontend/src/app/core/*`, созданную в рамках W0.2 (без UI/DI-зависимостей):
- `core/http/problem-details.spec.ts` — разбор `application/problem+json` (`detail`, `errors[]`, `title`, отсутствие тела, невалидное/не-объектное тело, ошибка без `HttpErrorResponse`), приоритет `detail → errors[] → title → fallback`.
- `core/money/money.spec.ts` — `isValidAmount`/`parseAmountInput`/`amountValidator`/`formatAmount`/`formatMoney`: произвольная точность без округления (ADR-0005), отрицательные суммы, запятая как разделитель, граничные невалидные форматы.
- `core/api/cursor-page.spec.ts` — `toCursorParams` (слияние с существующими `HttpParams`, пропуск пустого курсора/нулевого лимита) и `appendPage` (объединение страниц, иммутабельность, перенос `nextCursor`/`hasMore`).

Итого 52 новых теста (54 вместе с уже существующими в `app.spec.ts`), `ng test` — зелёный. Прогон через `@angular/build:unit-test` (в проекте фактически vitest под капотом, а не Karma/Jasmine — `toBeTrue`/`toBeFalse` недоступны, использован `toBe(true/false)`). Багов в реализации не найдено; единственная находка — `formatAmount` разделяет разряды неразрывным пробелом (U+00A0), не обычным, что для теста не баг, а особенность форматирования, которую нужно учитывать при написании новых тестов/E2E на отображение сумм.

**`tests/LupexWallet.UnitTests`** (103 теста) — домен и application-обработчики через прямые вызовы/моки портов (NSubstitute), без БД:
- `Wallets/WalletTests.cs`, `Wallets/PrimaryWalletPolicyTests.cs` — создание (первый кошелек → primary + IncludeInTotal, Q7/Q8), `Archive` (нельзя архивировать primary), `MarkAsPrimary`/`UnmarkAsPrimary`, `ChangeCurrency` (нельзя при `hasHistory`), `EnsureCanBeDeleted`, `ApplyBalanceDelta` (валюта должна совпадать), `UpdateDetails` (IncludeInTotal нельзя выключить у primary).
- `Operations/OperationTests.cs`, `TransferTests.cs`, `OperationEffectCalculatorTests.cs`, `OperationDateValidatorTests.cs`, `BehaviorKindMappingTests.cs` — Income/Expense/Adjustment (оба режима корректировки), защита операций-частей перевода, границы `OperationDate` (`AccountingStartDate`..сегодня), поведение `Transfer` нельзя создать напрямую (регресс — раньше падало 500, ADR/PROGRESS выше).
- `Operations/CreateOperationCommandHandlerTests.cs`, `DeleteOperationCommandHandlerTests.cs`, `CreateTransferCommandHandlerTests.cs`, `DeleteTransferCommandHandlerTests.cs` — оркестрация лукапов ReferenceData/Wallets и применение дельты баланса через `IWalletBalanceGateway` (ADR-0007) на моках портов.
- `ReferenceData/WalletTypeTests.cs`, `OperationTypeTests.cs`, `CurrencyTests.cs`, `CreateCurrencyCommandHandlerTests.cs` — create/deactivate/`EnsureCanBeDeleted`, глобальная уникальность кода валюты.
- `BalanceHistory/BalanceSnapshotTests.cs`, `BalanceRecalculationServiceTests.cs` — каскадный алгоритм пересчета (диапазон `[max(fromDate, AccountingStartDate)..сегодня]`, InitialBalance + накопленные дельты), advisory lock перед чтением (`AcquireWalletLockAsync`), "не трогать" снепшот при неизменившемся балансе (чтобы не плодить лишние события).

**`tests/LupexWallet.IntegrationTests`** (39 тестов, реальный Postgres + реальный HTTP API) — новые файлы `Wallets/WalletsApiTests.cs`, `ReferenceData/ReferenceDataApiTests.cs`, `Operations/OperationsApiTests.cs`, `Operations/TransfersApiTests.cs`, `BalanceHistory/BalanceHistoryApiTests.cs`:
- Сквозной сценарий: доход → расход → обе корректировки (Absolute/Delta) → изменение → удаление со сверкой `Wallet.CurrentBalance` на каждом шаге (воспроизводит вживую проверенный сценарий).
- Перевод: атомарность пары операций, защита операций-частей перевода от прямого изменения/удаления через `/operations`, удаление перевода реверсирует обе дельты.
- BalanceHistory: каскадный пересчет `/balance-history` при создании/изменении/удалении операции задним числом (в т.ч. обновление уже материализованных слепков), курсорная пагинация без дубликатов между страницами, `from > to` → 400, баланс раньше `AccountingStartDate` → 0 (Q13).
- ReferenceData: create/deactivate/delete WalletType/OperationType/Currency, `OperationBehaviorKind` — системный сид (4 строки, коды Income/Expense/Transfer/Adjustment).

**Изменения в тестовой инфраструктуре, потребовавшиеся для покрытия** (не бизнес-логика, но важно знать при добавлении новых интеграционных тестов):
- `IntegrationTestBase.AuthenticateAsync()` больше не логинится на каждый тест: `/auth/login` защищен ограничением частоты (`RateLimiting.cs`, 5 попыток/минуту, единое окно на весь Host/коллекцию тестов) — при десятках тестов, логинящихся индивидуально, лимит исчерпывался за миллисекунды (429 на большинстве тестов). Теперь `ApiTestFixture.InitializeAsync()` логинится один раз на весь прогон коллекции и кэширует токен (`ApiTestFixture.Token`); `AuthenticateAsync()` только прикрепляет его к `Client`. `AuthTests.cs` (смоук на сам `/auth/login`) не затронут — по-прежнему логинится напрямую.
- `ApiTestFixture`: `operation_behavior_kinds` (`reference_data`) исключена из `Respawn`-сброса — это системный сид (4 строки, ddd-model.md §2.3), а не пользовательские данные; без исключения `Respawn.ResetAsync()` перед каждым тестом стирал бы и сид, и `GET /operation-behavior-kinds` возвращал бы 0 строк начиная со второго теста.
- `TestDataBuilder.cs` — общие HTTP-хелперы создания кошелька/справочников для интеграционных тестов (избегают дублирования подготовки данных в каждом тесте); автогенерируемые имя типа кошелька/код валюты уникальны на вызов (`ux_wallet_types_active_name`/`ux_currencies_code` — реальные уникальные индексы схемы, нарушались при повторном вызове с одинаковым именем по умолчанию в одном тесте).

**Сознательно не покрыто на момент этой записи (заглушки/TODO из раздела "Известные упрощения" выше — не баги, тестировать нечего)**: `isUsed: false` захардкожен в `Delete*Command` ReferenceData (кросс-модульная проверка использования не реализована); "дата ≤ последнего слепка" в `DeleteOperationCommand`/`DeleteTransferCommand` (ADR-0002, тот же TODO); Angular UI для Operations/Transfers/BalanceHistory (не реализован); `ExchangeRates`/`Audit`/`Reporting` (модули-заглушки, нет доменной логики). Все пункты этого абзаца, кроме Angular UI, закрыты позже — см. запись W2.5+W2.6 в конце файла.

**Найдено при покрытии, исправлено позже (W2.6)**: MINOR — `PATCH /operations/{id}` над операцией-частью перевода (`OperationPartOfTransferException`) возвращал 400 вместо 409, унифицировано с `DELETE /operations/{id}` — см. запись W2.5+W2.6 в конце файла.

---

# ПЛАН: доведение приложения до полного соответствия архитектуре и требованиям

Составлен `feature-planner` 2026-09-11 по запросу «делай приложение целиком». Это **мастер-план оставшегося объёма целиком** (не по одному пункту): ExchangeRates, Reporting, Audit, кросс-модульные валидации, весь оставшийся Angular UI, тесты. Источники: `docs/requirements/*`, `docs/architecture/ddd-model.md`, `docs/architecture/high-level-architecture.md`, ADR-0001…ADR-0008, `docs/api/openapi.yaml`, `docs/database/schema.md` + сверка с фактическим кодом `src/`, `frontend/`.

Артефакты по `CLAUDE.md`: план — этот файл; архитектурные решения — новые ADR в `docs/architecture/adr/`; тесты — `tests/LupexWallet.UnitTests` / `tests/LupexWallet.IntegrationTests`; код/UI — сами файлы.

## П.0. Вне объёма (сознательно)

- **`OperationEffectCalculator.ComputeBaseline`** (Absolute-корректировка считает базу от `Wallet.CurrentBalance`, а не от пересчитанной истории на дату) — решение пользователя: **остаётся как есть**. Не включать ни в один пункт плана, не «чинить попутно». Пункт в «Известных упрощениях» остаётся действительным и осознанным.

## П.1. Что реально осталось (сверка кода с контрактом при планировании)

Помимо уже известных пунктов, при планировании сверены `openapi.yaml` ↔ реализованные эндпоинты и обнаружен **ранее не зафиксированный пробел**:

- **Wallets: UC-02…UC-06 не реализованы на бэкенде.** В `Wallets.Application` есть только `CreateWalletCommand`, `ListWalletsQuery`, `GetWalletQuery`. Методы агрегата (`UpdateDetails`, `Archive`, `MarkAsPrimary`/`UnmarkAsPrimary`, `ChangeCurrency`, `EnsureCanBeDeleted`) существуют и покрыты unit-тестами, но команд и эндпоинтов `PATCH /wallets/{id}`, `DELETE /wallets/{id}`, `POST /wallets/{id}/archive`, `POST /wallets/{id}/set-primary`, `PUT /wallets/{id}/currency` — **нет**, хотя они объявлены в `openapi.yaml`. Это блокирует UI кошельков (UC-02…UC-06) и тесно связано с пунктом «hasHistory» (см. W0.1).
- Wallets UI (`frontend/src/app/features/wallets/`) — только список + создание; действий над кошельком нет.
- Модули `ExchangeRates`, `Reporting`, `Audit` — пустые заглушки (`*ModuleExtensions.cs` / `*EndpointsExtensions.cs` без содержимого), схем `exchange_rates` / `audit` в БД нет (миграций нет).

### Текущий граф зависимостей проектов (важен для всех решений ниже)

```
Wallets.Application        → Wallets.Domain                              (ни от кого не зависит)
ReferenceData.Application  → ReferenceData.Domain                        (ни от кого не зависит)
Operations.Application     → Operations.Domain, Wallets.Application, ReferenceData.Application
BalanceHistory.Application → BalanceHistory.Domain, Wallets.Application, Operations.Application
BalanceHistory.Infrastructure → + Operations.Domain (типы событий), Wallets.Application
ExchangeRates.Application  → ExchangeRates.Domain                        (пусто)
Reporting.Application      → SharedKernel                                (пусто)
Audit.Application          → Audit.Domain                                (пусто)
```

Направление «снизу вверх» (Wallets → Operations → BalanceHistory) уже зафиксировано. Все оставшиеся кросс-модульные проверки — **обратные** этому направлению, отсюда пункт W0.1.

## П.2. Волны выполнения

Принцип разбиения (по `CLAUDE.md`, «Делегирование и параллелизм»): независимость = непересекающиеся файлы/проекты/схема БД. Внутри волны пункты запускаются **одним сообщением с несколькими вызовами** суб-агентов. Между волнами — зависимость по контракту/файлам.

```
Волна 0 (блокеры)        W0.1 ADR-0009 ─┐            W0.2 frontend-shell
                                        │
Волна 1 (параллельно)    W1.1 ExchangeRates backend  W1.2 Wallets lifecycle (UC-02…06)
                         W1.3 UI Operations/Transfers W1.4 UI ReferenceData mgmt
                                        │
Волна 2 (параллельно)    W2.1 Reporting backend (←W1.1)  W2.2 Audit backend (←W1.1)
                         W2.3 UI BalanceHistory          W2.4 UI ExchangeRates (←W1.1)
                         W2.5 Кросс-модульные проверки (←W0.1, ←W1.2)
                         W2.6 MINOR: унификация 409
                                        │
Волна 3 (параллельно)    W3.1 UI Reporting (←W2.1)  W3.2 UI Audit (←W2.2)
                         W3.3 UI действий над кошельком (←W1.2)
                                        │
Волна 4                  W4.1 сквозная регрессия + ревью + документация
```

---

## ВОЛНА 0 — блокирующие решения

### W0.1. ADR-0009: обратные кросс-модульные read-контракты (цикл зависимостей) — **требует архитектурного решения**

**Scope: M (решение), исполнение — в W2.5.** Агент: `architecture-documenter` (+ `architecture-designer` skill). Артефакт: `docs/architecture/adr/0009-*.md`.

Проблема шире, чем зафиксировано в «Известных упрощениях»: **три** разных места требуют, чтобы «нижний» по графу модуль спросил «верхний», что невозможно прямой ссылкой:

| # | Кто спрашивает | Что нужно узнать | У кого | Блокирует |
|---|---|---|---|---|
| a | `Wallets.Application` | «есть ли у кошелька операции/слепки» (`hasHistory` для `DELETE /wallets/{id}` и `PUT /wallets/{id}/currency`, Q2/Q15) | Operations, BalanceHistory | W1.2 (UC-04, UC-06) |
| b | `Operations.Application` | «дата последнего слепка кошелька» (ADR-0002, `DeleteOperationCommand`/`DeleteTransferCommand`) | BalanceHistory | W2.5 |
| c | `ReferenceData.Application` | «используется ли элемент справочника» (`isUsed`, Q11) | Wallets, Operations, ExchangeRates | W2.5 |

Решение должно покрывать **все три случая единообразно**, а не только (b).

**Вариант A — «контракт публикует потребитель, реализует владелец данных».** Интерфейсы (`IWalletHistoryProbe`, `IWalletSnapshotBoundary`, `IReferenceItemUsageProbe`) объявляются в проекте *спрашивающего* модуля (`Wallets.Application` / `Operations.Application` / `ReferenceData.Application`), реализации — в `Infrastructure` модулей-владельцев данных (`Operations.Infrastructure`, `BalanceHistory.Infrastructure`, `Wallets.Infrastructure`, `ExchangeRates.Infrastructure`), которые уже ссылаются (или могут сослаться без цикла) на Application нижних модулей. Регистрация — в `Add<Owner>Module`.
- **+** Ноль новых проектов; ровно тот же приём, что уже применён для `IWalletBalanceGateway`/`IWalletDirectory` (порт объявлен там, где нужен; адаптер — там, где данные) — без изменения правил ADR-0006/0007.
- **+** Компилятор по-прежнему не даёт нижнему модулю видеть типы верхнего.
- **−** Семантическая странность: контракт «есть ли слепок» физически живёт в `Operations.Application`, хотя описывает данные BalanceHistory; при чтении кода владение неочевидно (лечится XML-doc и неймингом).
- **−** Множественные реализации одного «probe» разными модулями (случаи a и c) требуют либо композитной реализации (`CompositeWalletHistoryProbe` в Host), либо интерфейса на каждый источник — усложняет DI-композицию.

**Вариант B — отдельные `*.Contracts`-проекты у владельцев данных.** Новые тонкие проекты `LupexWallet.BalanceHistory.Contracts`, `LupexWallet.Operations.Contracts`, `LupexWallet.Wallets.Contracts` (зависят только от `SharedKernel`): интерфейс + DTO. Потребитель ссылается на `*.Contracts` владельца; реализация — в `*.Infrastructure` владельца. Цикла нет, т.к. `*.Contracts` ни на кого не ссылается.
- **+** Владение контрактом корректное (контракт принадлежит модулю-владельцу данных); масштабируется на любые будущие пары модулей без разбора направления.
- **+** Единый предсказуемый приём вместо «смотри, в каком направлении граф».
- **−** +2…3 проекта в решении (сейчас 30), давление на `dotnet-project-structure` («не плодить проекты»); для полной консистентности со временем захочется перенести туда и уже существующие `IWalletBalanceGateway`/`IWalletOperationsLookup` — миграция существующего кода, не входящая в этот план.
- **−** Требует явного правила «что можно класть в Contracts» (только интерфейсы + примитивные DTO), иначе Contracts превратится в свалку.

**Рекомендация планировщика (решение — за архитектором):** вариант A для случая (b) как минимальное изменение, но, поскольку случаи (a) и (c) требуют композиции из 2–3 источников, вариант B в сумме даёт более простой DI и меньше «магии» — при выборе B ограничить его тремя новыми проектами и не мигрировать существующие контракты в рамках этого плана.

**DoD W0.1:**
1. ADR-0009 написан (Статус: Принято), покрывает все три случая (a/b/c) и явно указывает выбранный механизм + отвергнутые альтернативы.
2. Указано, где регистрируются реализации (`Add*Module`) и как разрешается множественность источников для (a)/(c).
3. Обновлены `high-level-architecture.md` §2 (правило границы модуля — 4-й разрешённый способ связи либо уточнение 3-го) и ссылка из ADR-0007/ADR-0008.
4. Раздел «Известные упрощения» этого файла помечен как «закрывается W2.5».
5. Явно указано, что «пустой» ответ probe (модуль-источник не зарегистрирован) недопустим — иначе `hasHistory` тихо вернётся к `false`.

### W0.2. Каркас фронтенда (shell, роутинг, общие утилиты)

**Scope: M.** Агент: `ui-builder` (skill `angular-architect`). Зависимостей нет, идёт параллельно W0.1.

**Почему отдельным пунктом:** все UI-пункты волн 1–3 иначе конкурировали бы за одни и те же файлы (`app.routes.ts`, общий layout, сервис форматирования Money, обработка ошибок `problem+json`) — это единственная точка пересечения фронтенд-задач. Здесь она закрывается один раз, дальше каждая UI-задача трогает только свою папку `features/<name>/`.

**Scope:**
- Layout-оболочка с навигацией: Кошельки / Операции / Переводы / История баланса / Курсы валют / Отчёт / Справочники / Аудит.
- `app.routes.ts` — **сразу все lazy-маршруты** всех запланированных фич (каждая UI-задача только создаёт компонент по уже прописанному пути; файл маршрутов после этого пункта никем не редактируется).
- `core/http/problem-details.ts` — единый разбор `application/problem+json` → сообщение пользователю (сейчас разбор ошибок дублируется в `wallets.component.ts`).
- `core/money/money.ts` — парс/формат `Money` (строка с точкой, произвольная точность — ADR-0005; **не** приводить к `number` при вычислениях и отображении).
- `core/api/cursor-page.ts` — типы курсорной страницы + хелпер «загрузить ещё».
- Общий стиль таблиц/форм (переиспользовать существующий `wallets.component.scss` как основу).

**DoD:** `npm run build` и `ng test` зелёные; существующий экран кошельков переведён на shell + общие утилиты без регрессии; ни один маршрут не ведёт в 404 (незаполненные фичи — временная заглушка «в разработке», удаляемая соответствующей задачей).

**Файлы:** `frontend/src/app/app.routes.ts`, `frontend/src/app/app.ts`, новые `frontend/src/app/core/{http,money,api}/*`, `frontend/src/app/layout/*`.

**Выполнено (`ui-builder`, 2026-09-11):**
- `app.routes.ts` — все 8 путей волн 0–3 прописаны сразу под общим `ShellComponent` (guard `authGuard` на уровне родительского маршрута, не на каждом дочернем как раньше): `/wallets` (реальный компонент), `/operations`, `/transfers`, `/balance-history`, `/exchange-rates`, `/reporting`, `/reference-data`, `/audit` — заглушка `shared/under-construction/under-construction.component.ts` (заголовок раздела передаётся через `route.data.title` + `withComponentInputBinding()` в `app.config.ts`). `/` редиректит на `/wallets`, `**` — на `/`. Файл дальше не редактируется — каждая будущая UI-задача (W1.3, W1.4, W2.3, W2.4, W3.1, W3.2) только меняет свою запись `loadComponent`.
- `layout/shell.component.ts(.html/.scss)` — новая оболочка: шапка с брендом, навигация по всем 8 разделам (`routerLink`/`routerLinkActive`), кнопка «Выйти» (перенесена из `wallets.component` — единая точка логаута для всех разделов, а не только кошельков). `app.ts`/`app.html` не тронуты (по-прежнему только `<router-outlet>` на верхнем уровне — `app.spec.ts` не изменился и остаётся зелёным).
- `core/http/problem-details.ts` — `extractErrorMessage(error, fallback)`, обобщает паттерн `error.error?.detail ?? fallback`, ранее продублированный в `wallets.component.ts`; порядок фолбэков `detail → errors[].message → title → fallback` (схема `Problem`, `openapi.yaml`), при первом же элементе `detail` поведение идентично прежнему.
- `core/money/money.ts` — `Money` (перенесен из `wallets.models.ts`, единый источник), `isValidAmount`/`parseAmountInput`/`amountValidator` (Reactive Forms) и `formatAmount`/`formatMoney` — форматирование через строковые операции (разделители разрядов), без приведения к `number` (ADR-0005).
- `core/api/cursor-page.ts` — `CursorPageMeta`/`CursorPage<T>` (заменили дублирующиеся определения в `wallets.models.ts` и `reference-data.models.ts`), `toCursorParams`, хелпер `appendPage` для будущего «загрузить ещё» (понадобится Operations/BalanceHistory).
- Общие дизайн-токены (CSS custom properties: цвета, радиусы) вынесены в `src/styles.scss`; `wallets.component.scss` и `shell.component.scss` используют `var(--color-*)`/`var(--radius-*)` вместо хардкода хексов.
- **Регрессионная проверка на экране кошельков**: `wallets.component.ts` переведён на `extractErrorMessage` (оба места разбора ошибок) и `formatMoney` (отображение баланса в таблице); поле `initialBalanceAmount` приведено в соответствие с контрактом (`openapi.yaml`: `WalletCreateRequest.initialBalanceAmount` — `string, format: decimal`, ранее в коде было `number` — задним числом обнаруженное расхождение с контрактом) — `type="text"` + `amountValidator` вместо `type="number"`. Хедер с логотипом/логаутом убран из `wallets.component.html` (перенесен в shell), мини-формы быстрого добавления справочников не тронуты.
- Проверено: `ng build` и `ng test` зелёные; дев-сервер поднят на порту 4300, все 8 маршрутов + `/login` + несуществующий путь возвращают 200 (SPA), сборка без ошибок биндинга.

**Признано вне этого пункта (по W1.4):** отдельного экрана `features/reference-data/*.component.ts` пока нет (только `models`/`service`) — маршрут `/reference-data` временно ведёт на общую заглушку наравне с остальными; мини-формы в `wallets.component` остаются рабочим путём добавления справочников до реализации W1.4.

---

## ВОЛНА 1 — параллельно, 4 вызова

### W1.1. Backend-модуль ExchangeRates (ADR-0001)

**Scope: L.** Агент: `backend-services-engineer` (`dotnet-core-expert` + `dotnet-ddd-clean-architecture` + `dotnet-efcore-data`). Зависимостей нет. Схема БД `exchange_rates` не пересекается ни с чем.

**Scope:**
- `ExchangeRates.Domain`: агрегат `ExchangeRateQuote` (ddd-model §2.7), VO `ExchangeRateValue` (инвариант `Rate > 0`), события `ExchangeRatesUpdated` / `ExchangeRateUpdateFailed`.
- `ExchangeRates.Infrastructure`: `ExchangeRatesDbContext` + **новая EF Core миграция схемы `exchange_rates`** строго по `docs/database/schema.md` (две таблицы: `latest_exchange_rates` PK `(from,to)`, `historical_exchange_rates` PK `(from,to,rate_date)`, CHECK `rate > 0`, CHECK различия валют).
- Клиент Frankfurter (`IFrankfurterClient` в Application, реализация в Infrastructure через `IHttpClientFactory`, таймаут + 1–2 ретрая, **без** ключа). Запрос парами «валюта кошелька → основная валюта» (ADR-0001 п.4), исторический курс — `/{date}?base=..&symbols=..`.
- `RefreshExchangeRatesCommand` — единая точка для планового прогона и ручной кнопки (h-l-a §5). При ошибке источника: **не** трогать `fetched_at`/курс, вернуть последние известные (ADR-0001 п.6) и поднять `ExchangeRateUpdateFailed`.
- `ExchangeRateRefreshBackgroundService` — раз в сутки; **ручное обновление сбрасывает таймер** (ADR-0001 п.3; реализовать через сигнал/`Channel`, чтобы фоновый сервис перезапустил интервал). Самовосстановление при исключении — по образцу `BalanceSnapshotSchedulerHostedService` (try/catch на уровне прохода, хост не падает).
- Реакция на `PrimaryWalletChanged` (ADR-0001 п.5): подписчик в `ExchangeRates.Infrastructure` (ссылка на `Wallets.Domain` ради типа события — прецедент ADR-0008). **Важно: НЕ делать HTTP-запрос внутри обработчика события** — он выполняется в `TransactionScope` команды `SetPrimaryWallet`; обработчик только сигналит фоновому сервису, который обновит курсы вне транзакции. Зафиксировать это как **ADR-0011** (короткий, пишется этой же задачей).
- Новые read-контракты, нужные модулю: `ReferenceData.Application.ICurrencyLookup` (id → код, список активных валют — Frankfurter работает с кодами) и `Wallets.Application.IWalletCurrencySet` (набор валют кошельков + валюта основного кошелька). Оба — «сверху вниз» по графу, циклов нет, W0.1 не требуется.
- Публичный read-контракт для Reporting: **`ExchangeRates.Application.IExchangeRateLookup`** — `GetRateAsync(from,to)` (последний известный) и `GetOrFetchHistoricalRateAsync(from,to,date)` → `(rate, ratesAsOfDate)`; если точного курса на дату нет, возвращается ближайший предшествующий (UC-21) с фактической датой.
- API: `GET /api/v1/exchange-rates/latest`, `POST /api/v1/exchange-rates/refresh` — строго по `openapi.yaml` (включая `502` с телом `LatestExchangeRates` при недоступности Frankfurter).
- Регистрация в `src/Host/LupexWallet.Api/Program.cs` (`AddExchangeRatesModule`/`MapExchangeRatesEndpoints` — заглушки уже есть).

**DoD:**
1. `dotnet build` + миграция применяется на чистой БД; схема соответствует `schema.md` дословно (имена таблиц/колонок/констрейнтов).
2. `GET /exchange-rates/latest` до первого успешного обновления возвращает `lastSuccessfulUpdate: null` и пустой `rates` (не 500).
3. Ручное обновление при недоступном источнике: 502 + прежние курсы + **неизменный** `lastSuccessfulUpdate`; в логе — предупреждение, хост жив.
4. Ручное обновление сбрасывает суточный таймер (проверяемо unit-тестом планировщика на фейковых часах).
5. `IExchangeRateLookup` опубликован и зарегистрирован (потребитель появится в W2.1).
6. Валюта, не поддерживаемая Frankfurter, **не ломает весь прогон**: неуспешные пары пропускаются, успешные сохраняются (пробел ADR-0001 «Последствия» — решение зафиксировать в ADR-0011 или комментарием).
7. Денежные/курсовые значения сериализуются через `CultureInfo.InvariantCulture` (регресс из раздела «Баги» выше).
8. Тесты (W-tests): unit — доменные инварианты, политика fallback, сброс таймера; integration — оба эндпоинта с **подменённым** `HttpMessageHandler` (успех/ошибка/таймаут), без реального сетевого вызова.

**Файлы:** `src/Modules/ExchangeRates/**` (4 проекта), `src/Host/LupexWallet.Api/Program.cs`, `src/Modules/ReferenceData/ReferenceData.Application|Infrastructure` (+ICurrencyLookup), `src/Modules/Wallets/Wallets.Application|Infrastructure` (+IWalletCurrencySet), `docs/architecture/adr/0011-*.md`.

**Конфликт по файлам:** `Program.cs` (общий со всеми пунктами волны — редактируется 1–2 строками, конфликт маловероятен, но W1.1/W1.2 не должны переписывать файл целиком); `Wallets.Application` — пересекается с W1.2 (разные файлы: новый `IWalletCurrencySet.cs` vs новые команды; допустимо).

### W1.2. Wallets: завершение жизненного цикла (UC-02…UC-06)

**Scope: M.** Агент: `backend-services-engineer`. Зависимость: частичная от W0.1 (только для `hasHistory`).

**Scope:** команды + эндпоинты, объявленные в `openapi.yaml`, но отсутствующие в коде:
- `UpdateWalletCommand` → `PATCH /wallets/{id}` (UC-02; `WalletUpdateRequest`, без валюты; инвариант Q8 — у основного нельзя выключить `includeInTotal` → 409).
- `ArchiveWalletCommand` → `POST /wallets/{id}/archive` (UC-03; основной кошелёк → 409, Q7).
- `SetPrimaryWalletCommand` → `POST /wallets/{id}/set-primary` (UC-05; через существующий `PrimaryWalletPolicy`, архивный → 409, принудительный `includeInTotal = true`).
- `DeleteWalletCommand` → `DELETE /wallets/{id}` (UC-04; 409 при наличии истории **или** если кошелёк основной) — часть «есть ли история» реализуется механизмом W0.1 (случай a).
- `ChangeWalletCurrencyCommand` → `PUT /wallets/{id}/currency` (UC-06; 409 при наличии операций/слепков, Q15) — та же зависимость.

**Порядок внутри пункта:** сначала три команды без кросс-модульных проверок (можно стартовать, не дожидаясь ADR-0009), затем две с `hasHistory`. Если W0.1 к моменту старта не готова — реализовать `DELETE`/`PUT currency`, **явно** оставив точку подключения probe и **не** захардкоживая `false` (в отличие от текущего TODO в Operations — не плодить новый долг).

**DoD:** 1) все 5 эндпоинтов отвечают согласно openapi (коды 200/204/400/404/409); 2) события `WalletUpdated`/`WalletArchived`/`WalletDeleted`/`PrimaryWalletChanged`/`WalletCurrencyChanged` реально поднимаются (нужны Audit в W2.2 и ExchangeRates в W1.1); 3) инвариант «ровно один основной кошелёк» проверен интеграционным тестом на 3 кошельках; 4) `hasHistory` берётся из реального источника (после W0.1), никакой заглушки; 5) unit + integration тесты на каждый эндпоинт, включая отказные ветки.

**Файлы:** `src/Modules/Wallets/Wallets.Application/*Command.cs`, `Wallets.Api/WalletsEndpointsExtensions.cs`, `WalletContracts.cs`, тесты.

### W1.3. Angular UI: Operations + Transfers (UC-11…UC-17, UC-24)

**Scope: L.** Агент: `ui-builder`. Зависимость: W0.2. Backend готов.

**Scope:** `frontend/src/app/features/operations/` (+`transfers/`): список операций по кошельку с фильтрами (кошелёк, диапазон дат, тип) и курсорной пагинацией; форма создания операции — доход/расход (UC-11/UC-12) и корректировка с выбором режима **Absolute/Delta** (UC-24, Q3); редактирование (UC-13), удаление (UC-14) с понятным сообщением при 409 (часть перевода / дата ≤ последнего слепка); экран переводов — создание (UC-16: только кошельки одной валюты, подсказка про UC-17/Q9), просмотр, удаление; операции-части перевода помечены в списке и не редактируются напрямую.

**DoD:** 1) все перечисленные UC доступны из UI без обращения к Swagger; 2) суммы вводятся/отображаются без потери точности (строка, не `number`); 3) ошибки бэкенда показываются разобранным текстом из `problem+json`, а не «500»; 4) тип операции выбирается из активных `operation-types` с показом поведения (`behaviorKind`), форма корректировки появляется только для `Adjustment`; 5) `ng test` — зелёный, добавлены спеки на компоненты форм.

### W1.4. Angular UI: управление справочниками (UC-26, UC-27 + деактивация/удаление)

**Scope: M.** Агент: `ui-builder`. Зависимость: W0.2. Backend готов.

**Scope:** `frontend/src/app/features/reference-data/` — полноценный экран: три вкладки (типы кошельков, типы операций, валюты), список с признаком активности, создание, деактивация, удаление (обработка 409 «используется»); типы операций — с выбором `OperationBehaviorKind` (системный справочник, только чтение). Мини-формы на экране кошельков остаются (быстрое добавление), но переиспользуют общий сервис.

**DoD:** 1) UC-26/UC-27 + деактивация + удаление доступны; 2) удаление используемого элемента показывает внятную ошибку (после W2.5 бэкенд начнёт реально возвращать 409 — UI должен быть к этому готов **уже сейчас**); 3) деактивированные элементы не предлагаются в формах создания кошелька/операции; 4) `ng test` зелёный.

---

## ВОЛНА 2 — параллельно, до 6 вызовов

### W2.1. Backend-модуль Reporting (UC-19, UC-21)

**Scope: M.** Агент: `backend-services-engineer`. **Зависит от W1.1** (`IExchangeRateLookup`).

**Scope:** только `Reporting.Application` + `Reporting.Api` + тонкая `Reporting.Infrastructure` (без Domain, без своей схемы — h-l-a §2/§3).
- `GetTotalAmountQuery(date?)` → `GET /api/v1/reporting/total-amount` (без `date` — текущая, UC-19; с `date` — историческая, UC-21).
- Текущая сумма: кошельки с `includeInTotal = true` (архивные включаются — Q16: флаг независим от архивности; **не** фильтровать по `IsArchived`), баланс → конвертация в валюту основного кошелька по последнему известному курсу.
- Историческая: баланс каждого кошелька на дату из BalanceHistory (для даты < `AccountingStartDate` → 0, Q13), курс на дату или последний известный **до** этой даты; `ratesAsOfDate` в ответе = фактически применённая дата курса.
- Новые read-контракты: `Wallets.Application.IWalletTotalsSource` (`WalletId`, `CurrencyId`, `CurrentBalance`, `IncludeInTotal`) и `BalanceHistory.Application.IWalletBalanceOnDateLookup` (баланс кошелька на дату). Направление «сверху вниз» (Reporting — самый верхний модуль), циклов нет.
- `Reporting.Application` получает ProjectReference на `Wallets.Application`, `BalanceHistory.Application`, `ExchangeRates.Application`.

**DoD:** 1) эндпоинт соответствует схеме `TotalAmount` (включая `ratesAsOfDate`); 2) кошелёк в валюте основного кошелька не конвертируется (курс 1:1, без похода в ExchangeRates); 3) отсутствие курса для пары — поведение явно определено и задокументировано (предложение: исключить кошелёк из суммы нельзя → вернуть 409/`problem+json` с указанием валюты; финальное решение — за реализующим агентом, зафиксировать в ADR или api-design); 4) интеграционный тест: 3 кошелька в 2 валютах + подменённый курс → точная ожидаемая сумма; 5) историческая сумма на дату до начала учёта всех кошельков = 0.

**Риск-заметка:** `GetOrFetchHistoricalRate` может **писать** в БД внутри обработки GET-запроса (`historical_exchange_rates` — append-only кэш). `TransactionBehavior` оборачивает только команды (`ICommand<T>`), поэтому запись пойдёт вне общей транзакции — это допустимо (кэш), но должно быть явно отмечено в коде и в тесте (повторный запрос той же даты не делает второй HTTP-вызов).

### W2.2. Backend-модуль Audit (UC-25) — **включает ADR-0010**

**Scope: L.** Агент: `backend-services-engineer`. Зависит от W1.1 только частью «события ExchangeRates» (можно двумя заходами: сначала 4 существующих модуля, затем ExchangeRates).

**Scope:**
- `Audit.Domain`: `AuditEntry` (append-only), VO `AuditActor` (`User`/`System` + `SystemProcessName`), `AuditFieldChange`.
- `Audit.Infrastructure`: `AuditDbContext` + миграция схемы `audit` строго по `schema.md`, **включая `CREATE RULE audit_entries_no_update/no_delete`** (raw SQL в миграции — EF сам такое не генерирует) и индекс `ix_audit_entries_entity`.
- Подписчики (`INotificationHandler<DomainEventNotification<T>>`) на **все** доменные события: Wallets (`WalletCreated/Updated/Archived/Deleted`, `PrimaryWalletChanged`, `WalletCurrencyChanged`, `WalletBalanceChanged`), ReferenceData (9 событий), Operations (`OperationCreated/Updated/Deleted`, `TransferCreated/Deleted`), BalanceHistory (`BalanceSnapshotCreated/Updated`), ExchangeRates (`ExchangeRatesUpdated`, `ExchangeRateUpdateFailed`). Обработчики — **в `Audit.Infrastructure`** (как у BalanceHistory, ADR-0008), там же ProjectReference на `*.Domain` модулей-издателей.
- `GET /api/v1/audit-entries?entityType&entityId` с курсорной пагинацией, сортировка `occurred_at DESC` — по `openapi.yaml`.
- Определение `AuditActor`: `User` для HTTP-запроса, `System` + имя процесса для фоновых задач (`BalanceSnapshotSchedulerHostedService`, `ExchangeRateRefreshBackgroundService`) — нужен способ протащить контекст (предложение: scoped `IAuditActorAccessor`, устанавливаемый middleware/фоновым сервисом).

**Открытое инженерное решение → ADR-0010 (пишет эта же задача):** существующие доменные события несут **только идентификаторы** (`WalletUpdated(WalletId)`), а требование UC-25 и схема `audit_entries.changes` требуют значений **до/после**. Варианты:
- **(1) Обогатить события** полями old/new — правки во всех `*.Domain` модулей (конфликтует по файлам с другими волнами, раздувает события).
- **(2) Снимать diff из EF Core `ChangeTracker`** отдельным `SaveChangesInterceptor` в `BuildingBlocks.Infrastructure`: событие даёт `entityType`/`entityId`/`action`/актора, а `changes` — из `EntityEntry.OriginalValues/CurrentValues` того же `SaveChanges`. Ничего не меняется в Domain, поле-уровневый diff получается бесплатно и единообразно.
- **(3) Гибрид**: (2) по умолчанию + точечное обогащение событий там, где diff из ChangeTracker бессмысленен (`ExchangeRateUpdateFailed`, `RatesUpdated`).
Рекомендация планировщика: **(3)**, но решение — за реализующим агентом/архитектором; зафиксировать в ADR-0010.

**DoD:** 1) миграция схемы `audit` применяется, UPDATE/DELETE по таблице физически не проходят (интеграционный тест на raw SQL); 2) каждое мутирующее API-действие порождает ≥1 `AuditEntry` в **той же транзакции** (инвариант ddd-model §5: откат команды откатывает и аудит) — проверить интеграционным тестом на откате; 3) `GET /audit-entries` возвращает историю по `(entityType, entityId)` с курсорной пагинацией без дублей между страницами; 4) `actorKind = System` для записей фоновых задач, `User` — для HTTP; 5) каскадный пересчёт истории баланса **не** порождает лавину бесполезных записей — политика (одна запись на прогон пересчёта vs запись на каждый слепок) явно выбрана и обоснована в ADR-0010 (ddd-model §6 требует запись на слепок — если оставляем так, зафиксировать ожидаемый объём как риск); 6) unit + integration тесты.

**Файлы:** `src/Modules/Audit/**`, возможно `src/BuildingBlocks/BuildingBlocks.Infrastructure/*` (при варианте 2/3), `Program.cs`, `docs/architecture/adr/0010-*.md`.

**Конфликт по файлам с W2.1:** только `Program.cs` (по 1–2 строки) — допустимо.

### W2.3. Angular UI: BalanceHistory (UC-18, UC-20, UC-23)

**Scope: M.** Агент: `ui-builder`. Зависимость: W0.2. Backend готов.
**Scope:** баланс кошелька на выбранную дату (`GET /wallets/{id}/balance?date=`), таблица/график истории за диапазон (`/balance-history`, курсорная пагинация), пояснение «баланс до даты начала учёта = 0» (Q13).
**DoD:** UC-18/UC-20 доступны из UI; `from > to` не отправляется на сервер (клиентская валидация) и корректно обрабатывается при 400; точность сумм сохранена; `ng test` зелёный.

### W2.4. Angular UI: ExchangeRates (UC-08, UC-09, UC-10)

**Scope: S.** Агент: `ui-builder`. **Зависит от W1.1.**
**Scope:** список последних курсов (коды валют подтягиваются из `/currencies` — в `ExchangeRateQuote` только id), метка времени последнего успешного обновления (UC-10), кнопка ручного обновления (UC-09) с обработкой 502 («источник недоступен, показаны последние известные курсы»).
**DoD:** три UC доступны; 502 не выглядит как поломка приложения; `ng test` зелёный.

### W2.5. Кросс-модульные проверки (закрытие TODO) — по ADR-0009

**Scope: L.** Агент: `backend-services-engineer`. **Зависит от W0.1 (решение) и W1.2 (файлы Wallets.Application).**
**Scope:** реализовать по выбранному в ADR-0009 механизму:
- (a) `hasHistory` для `DELETE /wallets/{id}` и `PUT /wallets/{id}/currency`;
- (b) «дата ≤ дате последнего слепка» в `DeleteOperationCommand` / `DeleteTransferCommand` (убрать захардкоженный `false`);
- (c) `isUsed` в `DeleteWalletTypeCommand` / `DeleteOperationTypeCommand` / `DeleteCurrencyCommand` (убрать захардкоженный `false` в `WalletTypes.cs`/`OperationTypes.cs`/`Currencies.cs`);
- (d) валидация существования и активности `walletTypeId`/`currencyId` при создании/изменении кошелька (сейчас не проверяется вовсе).
**DoD:** 1) ни одного `hasHistory: false`/`isUsed: false`-заглушки в коде (проверяется grep'ом); 2) на каждый пункт (a)–(d) — интеграционный тест на 409/400 и на разрешённый сценарий; 3) обновлён `tests/.../*` регрессионный набор, ранее зафиксировавший заглушки; 4) раздел «Известные упрощения» этого файла обновлён (пункты сняты); 5) производительность: проверки — точечные `EXISTS`-запросы, не выгрузка коллекций.

### W2.6. MINOR: унификация кода ответа 409 для операции-части перевода

**Scope: S.** Агент: `backend-services-engineer` (+ `api-designer` на правку контракта). Зависимостей нет, но файл `OperationsEndpointsExtensions.cs` пересекается с W2.5(b) — **выполнять последовательно с W2.5** либо одним агентом.
**Scope:** `PATCH /operations/{id}` при `OperationPartOfTransferException` → **409** (сейчас 400); обновить `docs/api/openapi.yaml` (добавить `409` в `PATCH /operations/{id}` с тем же описанием, что у `DELETE`); переписать регрессионный тест `TransfersApiTests.UpdateOperation_PartOfTransfer_IsRejectedAsDomainValidationError` под 409; снять пункт из раздела «Найдено при покрытии, не исправлено».
**DoD:** openapi проходит `redocly lint` с 0 ошибок; тест зелёный; UI (W1.3) корректно показывает сообщение для 409.

---

## ВОЛНА 3 — параллельно, 3 вызова

### W3.1. Angular UI: Reporting (UC-19, UC-21)
**Scope: S/M.** Агент: `ui-builder`. **Зависит от W2.1.** Total Amount на экране кошельков/дашборде (с указанием валюты основного кошелька), выбор даты → историческая сумма с подписью фактической даты курса (`ratesAsOfDate`). **DoD:** оба UC доступны; сумма и валюта отображаются согласованно с `GET /reporting/total-amount`; ошибка «нет курса» показывается человеческим текстом.

### W3.2. Angular UI: Audit (UC-25)
**Scope: S/M.** Агент: `ui-builder`. **Зависит от W2.2.** Просмотр истории изменений записи — вызывается из карточки кошелька / строки операции / перевода / слепка (кнопка «История изменений»), таблица: что изменилось (поле, было→стало), когда, кто (User/System). **DoD:** UC-25 доступен минимум для Wallet, Operation, Transfer, BalanceSnapshot; пагинация работает; пустая история отображается явно, а не как ошибка.

### W3.3. Angular UI: действия над кошельком (UC-02…UC-06)
**Scope: M.** Агент: `ui-builder`. **Зависит от W1.2.** Редактирование кошелька, архивирование, назначение основным, смена валюты, удаление — с обработкой всех 409 (основной нельзя архивировать/удалить, история блокирует смену валюты/удаление). **DoD:** пять UC доступны; фильтр «показывать архивные» уже существует — интегрировать с новыми действиями; `ng test` зелёный.

---

## ВОЛНА 4 — закрытие

### W4.1. Сквозная регрессия, ревью, документация
**Scope: M.** Агенты: `tdd-test-engineer` → параллельно `code-quality-reviewer` + `ux-parity-reviewer` → `architecture-documenter`.
- `dotnet test` обоих тестовых проектов и `ng test` — зелёные; новые интеграционные тесты не ломают `Respawn`-сброс (новые системные сиды, если появятся, исключить из сброса — прецедент `operation_behavior_kinds`).
- `code-quality-reviewer` (+ `dotnet-api-compatibility`: `openapi.yaml` — источник истины; `postgres-pro`/`database-optimizer` — ревью новых миграций `exchange_rates`/`audit`).
- `ux-parity-reviewer`: эталон — `docs/requirements/user-scenarios.md` + `docs/api/api-design.md`; проверка, что **все UC-01…UC-27** имеют путь в UI.
- `architecture-documenter`: ADR-0009/0010/0011 в финальном виде, обновление `high-level-architecture.md` (§2 правило границы, §5 таблица фоновых задач — добавить реальное имя сервиса ExchangeRates), `ddd-model.md` §6 (если политика аудита каскадного пересчёта изменена), таблица «Пройденные этапы» этого файла.
- Ручная проверка вживую (по образцу предыдущих срезов): реальный PostgreSQL + реальный Frankfurter (**сетевой доступ**, см. риски).

---

## П.3. Сводная таблица пунктов

| # | Пункт | Scope | Агент | Зависит от | Волна |
|---|---|---|---|---|---|
| W0.1 | ADR-0009: обратные кросс-модульные контракты | M | architecture-documenter | — | 0 |
| W0.2 | Каркас фронтенда (shell/routes/утилиты) | M | ui-builder | — | 0 |
| W1.1 | ExchangeRates backend + ADR-0011 | L | backend-services-engineer | — | 1 |
| W1.2 | Wallets UC-02…UC-06 (backend) | M | backend-services-engineer | частично W0.1 | 1 |
| W1.3 | UI Operations + Transfers | L | ui-builder | W0.2 | 1 |
| W1.4 | UI управление справочниками | M | ui-builder | W0.2 | 1 |
| W2.1 | Reporting backend | M | backend-services-engineer | W1.1 | 2 |
| W2.2 | Audit backend + ADR-0010 | L | backend-services-engineer | W1.1 (часть) | 2 |
| W2.3 | UI BalanceHistory | M | ui-builder | W0.2 | 2 |
| W2.4 | UI ExchangeRates | S | ui-builder | W1.1, W0.2 | 2 |
| W2.5 | Кросс-модульные проверки (a–d) | L | backend-services-engineer | W0.1, W1.2 | 2 |
| W2.6 | MINOR: 409 для PATCH /operations | S | backend-services-engineer | последовательно с W2.5 | 2 |
| W3.1 | UI Reporting | S/M | ui-builder | W2.1 | 3 |
| W3.2 | UI Audit | S/M | ui-builder | W2.2 | 3 |
| W3.3 | UI действий над кошельком | M | ui-builder | W1.2 | 3 |
| W4.1 | Регрессия + ревью + документация | M | tdd/reviewers/documenter | все | 4 |

Тесты (`tdd-test-engineer`) — **не отдельная волна**: вызываются сразу после реализующего агента внутри той же волны для каждого пункта (unit в `tests/LupexWallet.UnitTests/<Модуль>/`, integration в `tests/LupexWallet.IntegrationTests/<Модуль>/` по существующей структуре). Волна 4 — только сквозная регрессия поверх всего.

## П.4. Риски

1. **Внешняя зависимость Frankfurter недоступна в CI/песочнице** (сеть). Интеграционные тесты ExchangeRates обязаны использовать подменённый `HttpMessageHandler`; реальный вызов — **только ручная проверка** (раздел «ручная проверка» ниже). Риск: «зелёные тесты при нерабочем реальном клиенте» (неверный URL/парсинг ответа).
2. **HTTP внутри транзакции.** Реакция на `PrimaryWalletChanged` (ADR-0001 п.5) и `GetOrFetchHistoricalRate` из GET-запроса Reporting — потенциально долгий сетевой вызов внутри/рядом с `TransactionScope`. Смягчение: сигнал фоновому сервису (W1.1), таймауты, отсутствие ретраев внутри транзакции.
3. **Объём аудита.** Каскадный пересчёт истории (ADR-0003) на кошелёк с длинной историей порождает десятки-сотни `BalanceSnapshotUpdated` за одну пользовательскую правку; при записи «одна `AuditEntry` на слепок» (ddd-model §6) журнал растёт быстро и замедляет транзакцию. Решение — в ADR-0010.
4. **Неизменяемость `audit_entries` через `CREATE RULE`** ломает привычные сценарии тестов и `Respawn`-сброс (DELETE «молча ничего не делает»). Нужна явная стратегия очистки в тестах (TRUNCATE/отключение правил в тестовой БД) — иначе тесты будут видеть чужие записи.
5. **Цикл зависимостей (W0.1)** — если выбрать «локальный» фикс только под случай (b), случаи (a)/(c) через месяц потребуют второго решения. Риск архитектурного долга; смягчается требованием «ADR покрывает все три случая».
6. **Отсутствие курса для пары валют** (валюта не поддерживается Frankfurter, ADR-0001 «Последствия» — поведение не описано требованиями). Затрагивает Reporting (UC-19/UC-21 могут стать невычислимыми). Это **бизнес-неоднозначность**: если реализующий агент не сможет закрыть её ссылкой на требования — `CLARIFICATION_NEEDED` основному агенту (варианты: A — 409 с указанием валюты; B — исключить кошелёк из суммы с пометкой; C — считать курс 1:1).
7. **Параллельные правки `Program.cs`** (W1.1, W1.2, W2.1, W2.2) — точечные, но при одновременном запуске возможна гонка записи файла. Смягчение: каждый агент добавляет только свои 1–2 строки, не переформатирует файл.
8. **`app.routes.ts`** — единственная общая точка UI-задач; снимается пунктом W0.2 (все маршруты объявляются заранее).
9. **Регресс форматирования `Money`** (`CurrentCulture` вместо `InvariantCulture`) — уже случался в трёх модулях; новые API (ExchangeRates, Reporting, Audit) обязаны повторно проверяться на это в ревью.
10. **Рост числа проектов** при выборе варианта B в ADR-0009 (33 проекта вместо 30) — конфликт с `dotnet-project-structure`; смягчение: строгие правила содержимого `*.Contracts`.

## П.5. Ручная проверка (непроверяемо автотестами в текущей среде)

- **Реальный вызов Frankfurter** (сеть): `POST /exchange-rates/refresh` на запущенном хосте, проверка реальных курсов и `lastSuccessfulUpdate`; поведение при отключённой сети (эмулировать) — 502 + прежние курсы.
- **Суточный таймер автообновления** и его сброс ручным обновлением — в тестах проверяется на фейковых часах; реальный 24-часовой цикл не воспроизводится.
- **Реакция на смену основного кошелька** (перезапрос курсов с новой базой) — проверяется вживую, т.к. в тестах источник подменён.
- **Досоздание пропущенных слепков и плановый прогон 12:00 UTC** (уже существующая функциональность, затрагивается аудитом) — вживую при рестарте хоста.

## П.5.1. Решения пользователя (закрывают открытые вопросы плана)

- **Absolute-корректировка (`OperationEffectCalculator.ComputeBaseline`)** — остаётся как есть (см. П.0). Подтверждено пользователем 2026-09-11 при старте этого прогона.
- **Риск №6 (Reporting: валюта кошелька не поддерживается Frankfurter)** — решено пользователем 2026-09-11: кошелёк **исключается из Total Amount/исторической суммы** с явной пометкой в ответе API, какие кошельки исключены и почему (не 409, не курс 1:1). W2.1 обязан реализовать это как основной, а не запасной вариант: `TotalAmountResponse` включает список исключённых кошельков (id + причина), UI (W3.1) показывает это как предупреждение, а не ошибку.

## П.6. Указание основному агенту (оркестрация)

Прогонять конвейер **по волнам, а не по одному пункту**: внутри волны — один вызов на пункт, все вызовы волны **в одном сообщении**; после волны — фиксация итога в этом файле (раздел «Пройденные этапы» + снятие закрытых пунктов из «Известных упрощений»), затем следующая волна. Останавливаться и спрашивать пользователя — только по критериям «Критичный стоп» из `CLAUDE.md` (в этом плане потенциальный кандидат один — риск №6).

---

## W0.1 — ADR-0009 принят (2026-09-11)

**[ADR-0009](architecture/adr/0009-reverse-read-probes-via-consumer-owned-multi-implementation-ports.md)**: выбран вариант A (не B) — порт объявляется в `Application` модуля-потребителя, реализация — в `Infrastructure` модуля-владельца данных, регистрация — в `Add<Owner>Module`; это тот же приём, что уже даёт `IWalletBalanceGateway`/`IWalletDirectory`/`IWalletOperationsLookup` (ADR-0007/ADR-0008). Множественность источников (случаи a/c) решена без новых проектов и без composite-адаптера в Host: потребитель внедряет `IEnumerable<TПорт>` — DI сам собирает регистрации из разных `Add*Module`. Пустой список реализаций там, где ожидается ≥1 — ошибка конфигурации (fail-fast), а не молчаливый `false`.

Контракты для W1.2/W2.5 (объявлены — не реализованы):
- **(a)** `Wallets.Application.IWalletHistorySource.HasHistoryAsync(WalletId, ct) : Task<bool>` — реализации `Operations.Infrastructure.OperationsWalletHistorySource` (в `AddOperationsModule`) и `BalanceHistory.Infrastructure.BalanceHistoryWalletHistorySource` (в `AddBalanceHistoryModule`); потребитель — `DeleteWalletCommandHandler`/`ChangeWalletCurrencyCommandHandler` через `IEnumerable<IWalletHistorySource>` + `AnyAsync`.
- **(b)** `Operations.Application.IWalletSnapshotBoundary.GetLastSnapshotDateAsync(WalletId, ct) : Task<DateOnly?>` — реализация `BalanceHistory.Infrastructure.BalanceHistoryWalletSnapshotBoundary` (в `AddBalanceHistoryModule`); потребитель — `DeleteOperationCommandHandler`/`DeleteTransferCommandHandler`.
- **(c)** `ReferenceData.Application.IReferenceItemUsageProbe.IsUsedAsync(ReferenceItemKind, Guid, ct) : Task<bool>` (`ReferenceItemKind` = `WalletType|OperationType|Currency`) — реализации `Wallets.Infrastructure.WalletsReferenceItemUsageProbe` (в `AddWalletsModule`), `Operations.Infrastructure.OperationsReferenceItemUsageProbe` (в `AddOperationsModule`), позже `ExchangeRates.Infrastructure.ExchangeRatesReferenceItemUsageProbe` (в `AddExchangeRatesModule`, после W1.1); потребитель — `Delete{WalletType|OperationType|Currency}CommandHandler` через `IEnumerable<IReferenceItemUsageProbe>`.

`high-level-architecture.md` §2 (п.3) и ADR-0007/ADR-0008 («Связанные материалы») обновлены со ссылкой на ADR-0009.

## W1.4 — Angular UI управления справочниками завершён (2026-09-11)

`frontend/src/app/features/reference-data/` — экран с тремя вкладками (типы кошельков / типы операций / валюты): список с признаком активности (`Активен`/`Деактивирован`), создание, деактивация, удаление. Все HTTP-вызовы — через единый `ReferenceDataService` (уже переиспользуется мини-формами `features/wallets` и селектором типа операции в `features/operations`, дублирования нет). Работа продолжила прерванную ранее попытку — компонент/шаблон/стили/спека уже существовали почти в готовом виде, доделка ограничилась проверкой и точечными правками:

- Ошибки (в т.ч. будущий `409 isUsed`, пока backend всегда отдаёт `isUsed:false` — известное упрощение) разбираются через `extractErrorMessage` (`core/http/problem-details.ts`), тест на 409 при удалении используемого типа кошелька — зелёный.
- Типы операций: выбор `OperationBehaviorKind` через `creatableBehaviorKinds` — `Transfer` исключён из списка при создании нового типа (служебный, создаётся только парой операций перевода).
- Деактивированные элементы не предлагаются в формах создания кошелька/операции: `wallets.component.ts` и `operations.component.ts` вызывают `listWalletTypes`/`listOperationTypes`/`listCurrencies` без `includeInactive` — контракт `openapi.yaml` (`includeInactive: default false`) уже возвращает только активные элементы; экран управления справочниками явно передаёт `includeInactive: true`, чтобы показывать деактивированные с пометкой.
- Маршрут `/reference-data` в `app.routes.ts` уже указывал на реальный компонент (правка не потребовалась).
- Побочная находка: `operation-form.component.spec.ts` (W1.3, `features/operations`) использовал Jasmine-матчеры `toBeTrue()`/`toBeFalse()`, несовместимые с текущим тест-раннером (`@angular/build:unit-test` на vitest) — к моменту проверки уже был исправлен параллельно (заменён на `toBe(true/false)`), фиксирую здесь как найденный и не мой баг, чтобы не потерялось в истории.

Проверено: `ng build` — зелёный (7 lazy-чанков, включая `reference-data-component`); `ng test` — 6 test files / 67 тестов, все зелёные.

## W1.3 — Angular UI Operations + Transfers завершён (2026-09-11)

Работа продолжила прерванную ранее попытку — `frontend/src/app/features/operations/` (список с фильтрами по кошельку/типу/датам + курсорной пагинацией, форма создания/редактирования дохода/расхода/корректировки с режимом Absolute/Delta для `behaviorKind = Adjustment`, UC-11…UC-15/UC-24) уже существовала в готовом виде — доделка ограничилась проверкой (`ng build`/`ng test` перед изменениями) и одним найденным дефектом:

- `operation-form.component.spec.ts` использовал Jasmine-матчеры `toBeTrue()`/`toBeFalse()`, несовместимые с фактическим тест-раннером проекта (`@angular/build:unit-test` на vitest, а не Karma/Jasmine) — упавшая сборка тестов. Исправлено на `toBe(true/false)` (см. также параллельную находку W1.4 того же дефекта).

`frontend/src/app/features/transfers/` реализован с нуля (UC-16, UC-17):
- `transfers.models.ts`/`transfers.service.ts` — `Transfer`/`CreateTransferRequest`/`TransferPage` по `openapi.yaml`, `list`/`create`/`delete` через `GET|POST /transfers`, `DELETE /transfers/{id}`.
- `transfer-form/transfer-form.component.ts` — только создание (контракт не объявляет `PATCH /transfers/{id}`, только `DELETE`); список кошельков-получателей вычисляется (`computed`) от валюты выбранного кошелька-источника и заново фильтруется при её смене (UC-16 «только кошельки одной валюты», подсказка про UC-17/Q9, когда подходящих кошельков нет); кросс-валидатор формы `differentWalletsValidator` запрещает источник = получатель. Спека `transfer-form.component.spec.ts` добавлена по образцу `operation-form.component.spec.ts` (фильтрация целей по валюте, сброс цели при смене источника, `sameWallet`-валидация, submit/cancel).
- `transfers.component.{ts,html,scss}` — список переводов с фильтром по кошельку и курсорной пагинацией, создание через форму, удаление с разбором `409` (перевод учтён в истории баланса) через `extractErrorMessage`; стиль и структура — по образцу `operations.component`/`wallets` (общие CSS-токены, `entity-form`).
- `app.routes.ts` — заменены только `loadComponent` для `path: 'operations'` и `path: 'transfers'` (по 1 строке) на реальные компоненты вместо `UnderConstructionComponent`; остальной файл не тронут.

Денежные суммы — везде через `core/money/money.ts` (`Money`/`amountValidator`/`formatMoney`, строка, без `number`); ошибки — через `core/http/problem-details.ts`; пагинация — через `core/api/cursor-page.ts` (`appendPage`/`toCursorParams`). `shell.component.*` не изменялся (навигация на `/operations`/`/transfers` уже существовала).

Проверено: `ng build` — зелёный (добавлены lazy-чанки `operations-component`, `transfers-component`); `ng test` — 7 test files / 74 теста, все зелёные (67 существовавших + 7 новых в `transfer-form.component.spec.ts`).

## W1.2 — Wallets: завершение жизненного цикла (UC-02…UC-06) завершён (2026-09-11)

Работа продолжила прерванную ранее попытку — `IWalletHistorySource` (ADR-0009, случай a) и `UpdateWalletCommand`/`PATCH /wallets/{id}` уже существовали в готовом виде (проверены, не переписывались), как и домен (`Wallet.EnsureCanBeDeleted`/`Archive`/`MarkAsPrimary`/`ChangeCurrency`, `CannotDeletePrimaryWalletException`, `PrimaryWalletPolicy.SetPrimaryAsync`). Доделаны 4 оставшиеся команды + эндпоинты:

- `ArchiveWalletCommand` → `POST /wallets/{id}/archive` (200/404/409 — основной кошелёк, Q7).
- `SetPrimaryWalletCommand` → `POST /wallets/{id}/set-primary` (через `PrimaryWalletPolicy.SetPrimaryAsync`; 200/404/409 — архивный кошелёк).
- `DeleteWalletCommand` → `DELETE /wallets/{id}` (204/404/409 — история ИЛИ основной).
- `ChangeWalletCurrencyCommand` → `PUT /wallets/{id}/currency` (200/404/409 — история).

Реализованы обе реализации `IWalletHistorySource`, ожидавшиеся по ADR-0009 (их не было — созданы в этой сессии): `Operations.Infrastructure.OperationsWalletHistorySource` (`EXISTS` по `operations.operations.wallet_id`) и `BalanceHistory.Infrastructure.BalanceHistoryWalletHistorySource` (`EXISTS` по `balance_history.balance_snapshots.wallet_id`), зарегистрированы в `AddOperationsModule`/`AddBalanceHistoryModule`. Агрегация вынесена в переиспользуемый `WalletHistorySourceExtensions.AnyHasHistoryAsync` (пустой список реализаций → `InvalidOperationException`, а не `false`, — окончательный fail-fast guard на старте хоста остаётся за W2.5). Обе реализации используют транзитивную ссылку `*.Infrastructure → *.Application → Wallets.Application` — новых `ProjectReference` не потребовалось.

**Найден и исправлен реальный баг** (интеграционным тестом на 3 кошельках, инвариант «ровно один основной»): `PrimaryWalletPolicy.SetPrimaryAsync` выставляла `IsPrimary=true` новому кошельку и `false` старому в одном `SaveChangesAsync` — EF Core не гарантирует порядок `UPDATE` между несвязанными сущностями, и запрос иногда падал (500) из-за нарушения частичного уникального индекса `ux_wallets_single_primary` (не deferrable), если демонтаж старого основного отправлялся в БД позже назначения нового. Исправлено: снятие флага со старого основного теперь сохраняется отдельным `SaveChangesAsync` **до** назначения нового (оба вызова — в одной транзакции команды, откат по-прежнему атомарен).

Маппинг в `WalletsEndpointsExtensions.cs` (включая ранее не замапленный `PATCH`) и DTO (`WalletUpdateRequest`, `WalletChangeCurrencyRequest`) в `WalletContracts.cs` — по образцу `OperationsEndpointsExtensions` (`ValidationProblem`/`ConflictProblem`).

Тесты: `tests/LupexWallet.UnitTests/Wallets/WalletCommandHandlersTests.cs` (15 тестов на 5 обработчиков — NotFound/успех/доменные конфликты), `WalletHistorySourceExtensionsTests.cs` (агрегация `IEnumerable<IWalletHistorySource>`, включая fail-fast на пустом списке); `tests/LupexWallet.IntegrationTests/Wallets/WalletsApiTests.cs` дополнен 13 тестами на все 5 эндпоинтов (включая отказные ветки 404/409 и интеграционный тест на 3 кошельках для инварианта «ровно один основной»).

`dotnet build` всего решения и `dotnet test tests/LupexWallet.UnitTests/...` — зелёные (122 теста). `dotnet test tests/LupexWallet.IntegrationTests/...` **не прогнан до конца** в этой сессии: параллельно с этой работой другой агент вёл `ExchangeRates` (тот же `Program.cs`/DI), и на момент завершения W1.2 хост не поднимался целиком (`ILatestExchangeRateRepository` из `ExchangeRates.Application` ещё не зарегистрирован в `AddExchangeRatesModule` — это блокирует **весь** интеграционный набор, не только Wallets, включая уже существовавшие тесты `AuthTests`/`TransfersApiTests`). Сами проекты Wallets/Operations/BalanceHistory собираются в изоляции без ошибок. Требуется повторный прогон `dotnet test tests/LupexWallet.IntegrationTests/...` после завершения работы над ExchangeRates.

**Не реализовано осознанно (вне DoD W1.3):** UC-18…UC-23 (просмотр баланса/истории) — экран BalanceHistory (W2.3), не в этом пункте.

## W1.1 — Backend-модуль ExchangeRates завершён (2026-09-11)

Реализован с нуля (предыдущая попытка не оставила кода — только пустые csproj-заглушки/`AssemblyMarker.cs`, как и было зафиксировано при старте).

- **Domain**: `ExchangeRateQuote` ("последний известный" курс, идентифицируется парой `(FromCurrencyId, ToCurrencyId)`, без суррогатного Id — по аналогии с `BalanceSnapshot`), `HistoricalExchangeRate` (append-only кэш "на дату", тройка `(From, To, RateDate)`), VO `ExchangeRateValue` (инвариант `Rate > 0`), события `ExchangeRatesUpdated`/`ExchangeRateUpdateFailed` (батчевые, на уровне прогона `RefreshExchangeRates`, а не одного агрегата — см. обоснование в комментариях `ExchangeRateQuote.cs`/`RefreshExchangeRatesCommand.cs`).
- **Infrastructure**: `ExchangeRatesDbContext` + EF Core миграция схемы `exchange_rates` (сгенерирована `dotnet ef migrations add`, сверена построчно с `schema.md` — обе таблицы, составные PK, `CHECK`-констрейнты идентичны дословно). Клиент Frankfurter (`IFrankfurterClient`/`FrankfurterClient`) через `IHttpClientFactory`, таймаут 10с + до 3 попыток с нарастающей паузой (в решении нет Polly — ретраи вручную, как и остальные модули проекта). `FrankfurterTestableHandler` — Singleton-обёртка над `SocketsHttpHandler` с settable `TestOverride`, подставляемым в интеграционных тестах (реального сетевого вызова нет).
- `RefreshExchangeRatesCommand` — единая точка для планового прогона и ручной кнопки (`IsManualTrigger` определяет только необходимость сигнала фоновому сервису, не логику самого обновления). Обработчик — в `ExchangeRates.Infrastructure` (не в `Application`, как остальные команды проекта): ему нужен `IDomainEventDispatcher` (`BuildingBlocks.Infrastructure`) для прямой публикации батчевых событий, а Application-слой в этом решении на `BuildingBlocks.Infrastructure` не ссылается — тот же приём, что `BalanceHistory.Infrastructure.OperationEventHandlers` (MediatR-обработчик вне `*.Application`, регистрируется вручную в `Add*Module`, а не сканированием сборки).
- `ExchangeRateRefreshBackgroundService` — суточный цикл через чистый (юнит-тестируемый) `ExchangeRateRefreshTimer` (`ExchangeRates.Application`) + `IExchangeRateRefreshSignal`/`Channel` (`ExchangeRates.Infrastructure`) для двух разных сигналов: `NotifyManualRefreshCompleted` (ручное обновление уже произошло синхронно — просто перезапустить отсчёт) и `RequestRefresh` (обновление ещё не произошло — выполнить). Самовосстановление при исключении — try/catch на уровне прохода, по образцу `BalanceSnapshotSchedulerHostedService`.
- **Реакция на `PrimaryWalletChanged`** (ADR-0001 п.5) — `PrimaryWalletChangedHandler` только сигналит (`RequestRefresh`), не делает HTTP синхронно внутри транзакции `SetPrimaryWallet`. Решение зафиксировано отдельным **[ADR-0011](architecture/adr/0011-exchange-rate-refresh-decoupled-from-primary-wallet-transaction.md)**.
- **Новые межмодульные read-контракты** (направление "сверху вниз", ADR-0009 не требуется — циклов нет): `ReferenceData.Application.ICurrencyLookup` (код валюты по Id — нужен Frankfurter), `Wallets.Application.IWalletCurrencySet` (набор валют кошельков + валюта основного). Публичный контракт `ExchangeRates.Application.IExchangeRateLookup` (для Reporting, W2.1) реализован (`GetRateAsync`/`GetOrFetchHistoricalRateAsync` с fallback на ближайший ранее закэшированный курс, UC-21) и зарегистрирован, потребителя пока нет.
- **API**: `GET /api/v1/exchange-rates/latest`, `POST /api/v1/exchange-rates/refresh` строго по `openapi.yaml`, включая `502` с телом `LatestExchangeRates` при недоступности Frankfurter. `Program.cs` не трогался — точки композиции (`AddExchangeRatesModule`/`MapExchangeRatesEndpoints`) там уже были вызваны заранее (видимо, оставлено скелетом предыдущей волны).
- Валюта, не поддерживаемая Frankfurter (`FrankfurterUnsupportedCurrencyException`), пропускается молча — **не** считается отказом источника, не эскалируется до `502`/`ExchangeRateUpdateFailed` (в отличие от `FrankfurterUnavailableException` — сеть/таймаут/5xx, которая считается).
- `CultureInfo.InvariantCulture` — в сериализации курса в API-ответах (`ExchangeRateQuoteResponse.Rate`) и в сообщениях исключений; парсинг JSON от Frankfurter — через `System.Text.Json` (уже культурно-инвариантен).

**Найден и исправлен реальный баг при подключении тестовой инфраструктуры** (не в проде, но требовал архитектурного решения): `ExchangeRateRefreshBackgroundService` пробуждается по сигналу на **каждое** создание первого/основного кошелька (`PrimaryWalletChanged`), что в интеграционных тестах происходит в каждом тесте после `Respawn`-сброса — фоновый запрос к БД на отдельном потоке гонится с `ApiTestFixture.ResetAsync()` (TRUNCATE) между тестами и стабильно приводил к `40P01 deadlock detected` на стороне PostgreSQL (проявилось на уже существующем `WalletsApiTests`, не на новых тестах ExchangeRates). Решение — `LupexWalletApiFactory.ConfigureWebHost` теперь удаляет все `IHostedService` из тестового хоста (`RemoveAll<IHostedService>()`): бизнес-логика фоновых задач уже покрыта отдельно (юнит-тесты планировщиков; каскадный пересчёт BalanceHistory проверяется через синхронный путь CRUD операций, не через сам `BackgroundService`). Затронуло оба существующих фоновых сервиса (`BalanceSnapshotSchedulerHostedService` тоже отключён в тестах), регрессий не вызвало — прогнан полный набор.

**Тесты**: `tests/LupexWallet.UnitTests/ExchangeRates/` — `ExchangeRateValueTests`, `ExchangeRateQuoteTests` (доменные инварианты), `ExchangeRateRefreshTimerTests` (сброс суточного таймера на фейковых часах), `RefreshExchangeRatesCommandHandlerTests` (fallback-политика: недоступность источника сохраняет старый курс и репортит отказ, неподдерживаемая валюта пропускается без отказа, успешный прогон персистит курс и поднимает событие) — на моках портов (NSubstitute), без БД/HTTP. `tests/LupexWallet.IntegrationTests/ExchangeRates/ExchangeRatesApiTests.cs` — оба эндпоинта через `FrankfurterTestableHandler` (успех/недоступность/неподдерживаемая валюта), без реального сетевого вызова.

`dotnet build` всего решения — зелёный. `dotnet test tests/LupexWallet.UnitTests/...` — 139 тестов, зелёные. `dotnet test tests/LupexWallet.IntegrationTests/...` (реальный Docker/Postgres) — 56 тестов, зелёные (включая ранее отложенный полный прогон из W1.2 и регресс-проверку на deadlock).

**Известное упрощение, оставлено на W2.5**: `ExchangeRates.Infrastructure.ExchangeRatesReferenceItemUsageProbe` (ADR-0009, случай c — `isUsed` для `Currency`) не реализован в этом срезе: интерфейс `ReferenceData.Application.IReferenceItemUsageProbe` ещё не существует (сама W2.5 не запускалась). ADR-0009 прямо ожидает этот пробник "вместе с W1.1" — сознательно отложено до появления интерфейса, чтобы не вводить его в одностороннем порядке.

## W2.4 — Angular UI: ExchangeRates завершён (2026-09-11)

`frontend/src/app/features/exchange-rates/` — новая фича, backend полностью готов (W1.1), правки маршрутов не выходят за одну строку `app.routes.ts` (`loadComponent` под `/exchange-rates`).

- `exchange-rates.models.ts` — `ExchangeRateQuote`/`LatestExchangeRates` строго по `openapi.yaml` (курс — строка, ADR-0005).
- `exchange-rates.service.ts` — тонкий HTTP-клиент `GET /exchange-rates/latest`, `POST /exchange-rates/refresh`.
- `exchange-rates.component.ts` — список последних курсов (UC-08 виден как результат последнего прогона), метка времени последнего успешного обновления (UC-10: `lastSuccessfulUpdate = null` → «никогда», иначе `toLocaleString('ru-RU')`), кнопка «Обновить сейчас» (UC-09) с индикацией `isRefreshing`. Коды валют для отображения (`ExchangeRateQuote` хранит только `CurrencyId`) — через уже существующий `ReferenceDataService.listCurrencies` (`features/reference-data`), HTTP-вызов не задублирован.
- Обработка `502` от `/exchange-rates/refresh`: тело ответа — та же схема `LatestExchangeRates` с прежними курсами (не `problem+json`), поэтому обработчик ошибки применяет это тело как обычный успешный ответ (`applyLatest`) и отдельно показывает ненавязчивое предупреждение `sourceUnavailable` («Источник курсов недоступен, показаны последние известные курсы») — отдельно от `loadError`, чтобы не выглядеть как поломка приложения (DoD).
- Стиль экрана и структура компонента — по образцу `features/reference-data/` (signals, `OnPush`, `extractErrorMessage`, CSS-токены `var(--color-*)`/`var(--radius-*)`, без хардкода).
- Тесты: `exchange-rates.component.spec.ts` (5 тестов) — загрузка и сопоставление кодов валют, «никогда»/отформатированная метка времени, успешное ручное обновление, аккуратная обработка `502` (курсы сохранены, `loadError` пуст, показано дружелюбное предупреждение).

Проверено: полный `ng test` — зелёный (91/91, включая параллельно завершённый W2.3). `ng build` — зелёный, `exchange-rates-component` собирается отдельным lazy chunk (5.79 kB).

## W2.3 — Angular UI: BalanceHistory завершён (2026-09-11)

`frontend/src/app/features/balance-history/` — новая фича, backend полностью готов (срез "BalanceHistory (бэкенд)"), правки маршрутов не выходят за одну строку `app.routes.ts` (`loadComponent` под `/balance-history`).

- `balance-history.models.ts` — `BalanceSnapshot`/`BalanceSnapshotPage` строго по `openapi.yaml`, сумма через `core/money/money.ts` (`Money`, без приведения к `number`).
- `balance-history.service.ts` — тонкий HTTP-клиент `GET /wallets/{id}/balance?date=` (без даты — текущий баланс) и `GET /wallets/{id}/balance-history` (курсорная пагинация через `core/api/cursor-page.ts`).
- `balance-history.component.ts/html/scss` — выбор кошелька; секция «Баланс на дату» (UC-18/UC-20); секция «История за период» с таблицей и «Загрузить ещё» (`appendPage`, по образцу `features/operations/`); явная текстовая подсказка «баланс до даты начала учёта (`accountingStartDate`) кошелька считается равным 0» (Q13) плюс бейдж у строк/результата, дата которых раньше этой границы — чтобы не выглядело как пустая/необъяснённая строка; клиентская валидация `from > to` (не отправляется на сервер, `rangeValidationError`) и обработка серверного 400 через `extractErrorMessage`/`problem-details.ts`. UC-23 (каскадный пересчёт) — фоновая механика бэкенда, в UI не требует отдельной логики: таблица всегда показывает уже пересчитанный бэкендом результат.
- Стиль — по образцу `features/operations/` (signals, `OnPush`, реактивные формы, CSS-токены `var(--color-*)`/`var(--radius-*)`, без хардкода).
- Тесты: `balance-history.service.spec.ts` (3 теста — параметры запроса), `balance-history.component.spec.ts` (10 тестов — загрузка справочников, подсказка Q13, баланс на дату и бейдж «до начала учёта», клиентский запрет `from > to` без похода на сервер, история + `loadMore` по курсору, обработка серверного 400, сброс результатов при смене кошелька).

Проверено: `ng test` — 91/91 зелёные (все фичи, включая параллельно реализованный W2.4); `ng build` — зелёный (`balance-history-component` лениво подгружаемый чанк 12.20 kB).

## W2.1 — Backend-модуль Reporting завершён (2026-09-11)

`Reporting.Application` (полностью реализован) + `Reporting.Api`, `Reporting.Infrastructure` остаётся пустым намеренно (см. ниже) — GET `/api/v1/reporting/total-amount` (UC-19 без `date`, UC-21 с `date`).

- **Реализация риска №6 по решению пользователя** (docs/PROGRESS.md, П.5.1, зафиксировано 2026-09-11): кошелек, для которого `IExchangeRateLookup` не вернул курс, **исключается** из суммы — не 409, не курс 1:1. Причина и `walletId` попадают в `excludedWallets` ответа. `openapi.yaml` дополнен: `TotalAmount.excludedWallets` (обязательное поле) + новая схема `ExcludedWallet`.
- Новые read-контракты, направление "сверху вниз" (Reporting — самый верхний модуль, не случай ADR-0009): `Wallets.Application.IWalletTotalsSource` (все кошельки, включая архивные — Q16) и `BalanceHistory.Application.IWalletBalanceOnDateLookup` (баланс на дату, тот же алгоритм и Q13-правило, что и `GetWalletBalanceQueryHandler`, продублирован намеренно ради независимости двух точек входа). `Reporting.Application` получил `ProjectReference` на `Wallets.Application`/`BalanceHistory.Application`/`ExchangeRates.Application`.
- DoD п.2 (курс 1:1 без похода в ExchangeRates) реализован буквально: для кошелька в валюте основного `IExchangeRateLookup` вообще не вызывается (проверено unit-тестом через `DidNotReceiveWithAnyArgs`), а не полагается на короткое замыкание внутри `ExchangeRateLookup.GetRateAsync`.
- `ratesAsOfDate` при нескольких разных не-основных валютах — самая ранняя из фактически примененных дат курса (консервативная оценка "насколько устарели данные"); для текущей суммы (без `date`) — сегодняшняя дата, т.к. `GetRateAsync` не возвращает собственную дату курса. Явно задокументировано в коде как техническое решение (openapi.yaml не уточняет поведение при нескольких валютах).
- `AddReportingModule` не регистрирует ничего своего — реализации трёх портов уже регистрируются в `AddWalletsModule`/`AddBalanceHistoryModule`/`AddExchangeRatesModule`, `Program.cs` не потребовал правок (заглушки уже были на месте).
- **Побочный фикс чужого конфликта параллельной волны**: во время работы обнаружен нерабочий промежуточный build — параллельно выполнявшийся агент (W2.5/ADR-0009, случай c) добавил в `WalletsModuleExtensions.cs` использование `ReferenceData.Application.IReferenceItemUsageProbe`, не добавив `ProjectReference` на `ReferenceData.Application` в `Wallets.Infrastructure.csproj`. Добавлена только эта одна строка `ProjectReference` (без изменения чужой бизнес-логики) — иначе `dotnet build` всего решения не проходил ни для одной задачи волны.

**Тесты**: `tests/LupexWallet.UnitTests/Reporting/GetTotalAmountQueryHandlerTests.cs` (8 тестов, на моках портов) — нет основного кошелька → null, кошелек в основной валюте не дергает `IExchangeRateLookup`, конвертация и суммирование, исключение кошелька без курса с непустой причиной, `IncludeInTotal=false` не попадает ни в сумму, ни в `excludedWallets`, историческая дата с фактическим курсом, дата раньше `accountingStartDate` → 0. `tests/LupexWallet.IntegrationTests/Reporting/ReportingApiTests.cs` (3 теста, реальный Postgres) — 3 кошелька в 2 валютах с курсом через `FrankfurterTestableHandler` → точная сумма; кошелек без курса исключён с причиной, остальные посчитаны; историческая сумма на дату до `accountingStartDate` = 0.

## П.5.2. Решение пользователя по ADR-0009, случай (b) — граница "история" для удаления операции/перевода (зафиксировано 2026-09-11)

При реализации W2.5 обнаружено, что запланированная буквальная проверка UC-14/ADR-0002 «операцию, дата которой ≤ дате последнего слепка баланса кошелька, удалить нельзя» непригодна: каскадный пересчет (ADR-0003/ADR-0008) материализует слепок кошелька на дату **самой** операции немедленно при её создании — то есть «последний слепок» практически всегда ≥ дате любой существующей операции, и буквальное применение заблокировало бы подавляющее большинство удалений, включая только что созданную операцию (прямо расходится с UC-14). Порт `Operations.Application.IWalletSnapshotBoundary` (+ реализация `BalanceHistory.Infrastructure.BalanceHistoryWalletSnapshotBoundary`), предусмотренный ADR-0009 для этого случая, был объявлен, но не подключён — оставлен как `CLARIFICATION_NEEDED`.

**Решено пользователем**: граница «история» для `DELETE /operations/{id}` и `DELETE /transfers/{id}` — **сегодняшний день**, без обращения к `BalanceHistory`. Операция/перевод с датой = сегодня можно удалить всегда (при отсутствии прочих препятствий, например участия в переводе); с датой строго раньше сегодня — нельзя, только редактирование (UC-13). Проверка выполняется локально в `Operations.Application` (`DateOnly.FromDateTime(DateTime.UtcNow)`, тот же паттерн, что уже использует `OperationDateValidator`) — кросс-модульный запрос к `BalanceHistory` не понадобился. `IWalletSnapshotBoundary`/`BalanceHistoryWalletSnapshotBoundary` удалены как ненужные; ADR-0009 обновлён этим решением (раздел «Случай (b)»).

## W2.5 + W2.6 — Кросс-модульные проверки (a–d) и унификация 409 завершены (2026-09-11)

Продолжена прерванная ранее попытка — на момент старта уже были готовы: (a) `IWalletHistorySource` (W1.2, не в скоупе этого среза); (c) `IReferenceItemUsageProbe` + все три реализации (`WalletsReferenceItemUsageProbe`/`OperationsReferenceItemUsageProbe`/`ExchangeRatesReferenceItemUsageProbe`), `isUsed: false` в `Currencies.cs`/`OperationTypes.cs`/`WalletTypes.cs` реально убран; (d) `ICurrencyLookup`/`IWalletTypeLookup` подключены в `CreateWalletCommand`/`UpdateWalletCommand` с проверкой существования и активности; W2.6 — `PATCH /operations/{id}` уже возвращал 409 для `OperationPartOfTransferException`, `openapi.yaml` и регрессионный тест уже обновлены.

Доделан единственный оставшийся пункт — **случай (b)**, по решению пользователя из П.5.2 выше:
- `Operation.EnsureCanBeDeleted(bool hasHistory)` (было без параметра) — бросает новый `OperationDeletionNotAllowedException`, если `hasHistory`; `Transfer.EnsureCanBeDeleted(bool hasHistory)` уже существовал в нужном виде.
- `DeleteOperationCommand`/`DeleteTransferCommand` — `hasHistory = OperationDate/TransferDate < сегодня`, без обращения к `BalanceHistory`.
- `OperationsEndpointsExtensions.cs`: `DELETE /operations/{id}` теперь ловит `OperationPartOfTransferException or OperationDeletionNotAllowedException` → 409 (была только первая).
- Удалены `Operations.Application.IWalletSnapshotBoundary` и `BalanceHistory.Infrastructure.BalanceHistoryWalletSnapshotBoundary` вместе с регистрацией в `AddBalanceHistoryModule` — не потребовались.
- `docs/api/openapi.yaml` — описания `409` для `DELETE /operations/{id}` и `DELETE /transfers/{id}` уточнены под новую границу ("дата строго раньше сегодняшнего дня" вместо "дата ≤ дате последнего слепка").
- `docs/architecture/adr/0009-*.md` — раздел «Случай (b)» переписан: порт не вводился, причина и решение зафиксированы.

**Попутно исправлены два интеграционных теста, вступивших в противоречие с новым правилом** (оба создавали операцию/перевод с датой в прошлом и ожидали успешное удаление — до решения П.5.2 это было допустимо по старой заглушке `hasHistory: false`):
- `BalanceHistoryApiTests.DeleteOperation_CascadesRecalculationFromDeletedOperationDate` — дата удаляемой операции перенесена на сегодня (единственная дата, для которой удаление вообще разрешено новым правилом), проверка каскадного отката значения снепшота сохранена.
- `TransfersApiTests.UpdateOperation_PartOfTransfer_ReturnsConflict` — использовал случайный (несуществующий) `OperationTypeId`, из-за чего `UpdateOperationCommandHandler` отклонял запрос как `OperationTypeReferenceNotFoundException` (400) раньше, чем доходил до проверки «часть перевода»; заменён на реально существующий активный тип операции — тест теперь проверяет именно сценарий W2.6.

**Тесты**: unit — `OperationTests.cs` (2 новых теста на `EnsureCanBeDeleted(hasHistory)`), `DeleteOperationCommandHandlerTests.cs`/`DeleteTransferCommandHandlerTests.cs` (по одному новому тесту на отказ при дате в прошлом, без касания баланса); интеграционные — `OperationsApiTests.cs` (`DeleteOperation_DatedToday_ReturnsNoContent`, `DeleteOperation_DatedBeforeToday_ReturnsConflict`), `TransfersApiTests.cs` (`DeleteTransfer_DatedBeforeToday_ReturnsConflict`; положительный сценарий "сегодня" уже был покрыт существующим `DeleteTransfer_ReversesBothDeltasAndRemovesTransfer`). Пункты (a)/(c)/(d) уже имели интеграционное покрытие 409/400 + разрешённый сценарий (`WalletsApiTests.cs`, `ReferenceDataApiTests.cs`) — не дублировалось.

`grep -rn "isUsed: false|hasHistory: false" src/` — 0 совпадений. `dotnet build` всего решения — зелёный (0 warnings/errors). `dotnet test tests/LupexWallet.UnitTests` — 163/163 зелёных (было 160, +3). `dotnet test tests/LupexWallet.IntegrationTests` — 71/71 зелёных (Audit-миграция, ранее блокировавшая весь прогон, к моменту этого запуска уже была на месте — не потребовалось ждать W2.2 отдельно).

Раздел «Известные упрощения» выше обновлён — пункты (c)/(d) (кросс-модульная валидация ссылок) и (b) (точная граница "связанности") сняты как закрытые.

## W2.2 — Backend-модуль Audit завершён (2026-09-11)

Продолжена прерванная ранее попытка (техническим сбоем, не по существу) — на момент старта уже были готовы весь `Audit.Domain`/`Audit.Application`/`Audit.Api` и почти весь `Audit.Infrastructure` (DbContext, конфигурация, репозиторий, `AuditRecorder`/`AuditUnitOfWork`, все обработчики `*AuditHandlers.cs`, регистрация в `AuditModuleExtensions.cs`), `EntityFieldChange`/`AuditActorAccessor` (`BuildingBlocks.Infrastructure`) и ADR-0010 (полностью, включая раздел «Тестовая инфраструктура»). Доделаны три оставшихся технических пункта:

- **EF Core миграция `Audit.Infrastructure/Migrations/20260911083933_InitialCreate.cs`** — сгенерирована (`dotnet ef migrations add InitialCreate --context AuditDbContext`), построчно совпадает со схемой `audit.audit_entries` из `schema.md`. В `Up()` вручную добавлены `CREATE RULE audit_entries_no_update`/`audit_entries_no_delete ... DO INSTEAD NOTHING` (EF их не генерирует), в `Down()` — симметричные `DROP RULE`.
- `LupexWalletApiFactory.MigrateAllAsync()` — добавлен `AuditDbContext.Database.MigrateAsync()`.
- `ApiTestFixture` — реализовано решение из ADR-0010: `ResetAsync()` дополнительно выполняет `TRUNCATE TABLE audit.audit_entries;` после `_respawner.ResetAsync(...)` (схема `audit` намеренно не входит в `SchemasToInclude` — `CREATE RULE ... ON DELETE` превращает генерируемый Respawn `DELETE` в no-op). Добавлено публичное свойство `ApiTestFixture.ConnectionString` — нужно тестам, проверяющим `CREATE RULE` напрямую через raw SQL (через HTTP API это не выразимо, модуль Audit не имеет мутирующих эндпоинтов).

**Инженерное решение по DoD п.2 («откат команды откатывает и аудит»), не покрытое ADR-0010 буквально** — задокументировано прямо в тесте (`AuditApiTests.SetPrimaryWallet_OnArchivedWallet_RollsBackBothWalletStateAndAuditTrail`): детерминированный, доступный только через публичный HTTP API сценарий «шаг 1 команды физически сохраняет и диспетчеризует доменное событие (создавая AuditEntry), шаг 2 той же команды бросает доменную ошибку до финального `SaveChangesAsync`» в этом кодовой базе не нашёлся — большинство многошаговых команд либо проверяют инварианты до первого `SaveChangesAsync` (нечего откатывать), либо (`CreateOperationCommand`/`CreateTransferCommand`) не имеют доступной через API проверки между вложенными сохранениями. Использован ближайший реальный аналог: `POST /wallets/{id}/set-primary` на архивный кошелек — шаг 1 (`PrimaryWalletPolicy.UnmarkAsPrimary` + `SaveChangesAsync`) физически применяется к БД внутри `TransactionScope`, шаг 2 (`Wallet.MarkAsPrimary`) бросает `CannotSetArchivedWalletAsPrimaryException` до финального `SaveChangesAsync` — вся команда откатывается. Тест проверяет оба следствия одной и той же гарантии `TransactionBehavior`, на которой держится атомарность аудита: бизнес-состояние (старый основной кошелек остаётся основным) и отсутствие каких-либо новых записей в audit-трейле старого основного кошелька после неудачной попытки.

**Тесты**:
- `tests/LupexWallet.UnitTests/Audit/` (17 новых тестов) — `AuditEntryTests.cs` (валидация `Create`, монотонность `OccurredAt`), `AuditActorTests.cs` (`User`/`System`, обязательность `SystemProcessName`), `AuditRecorderTests.cs` (actorKind = User по умолчанию и System с/без имени процесса — W2.2 DoD п.4; `RecordFromChangeTrackerAsync` не падает при отсутствии предвычисленного диффа). Полный расчёт diff из EF Core `ChangeTracker` (`DispatchDomainEventsInterceptor.ComputeFieldChanges`) требует реального `DbContext` и целенаправленно не дублируется на уровне unit-тестов (в этом проекте нет прецедента EF-based unit-тестов, конвенция — Testcontainers/IntegrationTests) — он покрыт интеграционными тестами ниже, где `changes` реальных CRUD-команд видны в ответе `GET /audit-entries`.
- `tests/LupexWallet.IntegrationTests/Audit/AuditApiTests.cs` (4 новых теста) — неизменяемость таблицы (raw SQL `UPDATE`/`DELETE` на `audit.audit_entries` физически не проходят, 0 затронутых строк); мутирующая команда порождает `AuditEntry` в той же транзакции + откат команды откатывает и аудит (сценарий выше); курсорная пагинация `GET /audit-entries` без дублей между страницами (лимит 2, 6 записей на кошелек).
- `actorKind = System` для записи, порождённой фоновой задачей: полноценный E2E невозможен без реального `IHostedService` (в `LupexWalletApiFactory.ConfigureWebHost` фоновые сервисы намеренно отключены — `RemoveAll<IHostedService>()`, см. комментарий в файле), поэтому разметка актора протестирована на уровне `AuditRecorderTests` (прямой вызов с `IAuditActorAccessor`, возвращающим `IsSystem: true`) — тот же путь кода (`AuditRecorder.RecordAsync`), которым пользуются все обработчики `*AuditHandlers.cs`.

`dotnet build` всего решения — зелёный (0 warnings/errors). `dotnet test tests/LupexWallet.UnitTests` — 177/177 зелёных (было 160, +17). `dotnet test tests/LupexWallet.IntegrationTests` — 75/75 зелёных (было 71 после W2.5/W2.6 без реальной Audit-миграции — при первом прогоне после доделки этой задачи 54/68 падали с `42P01: relation "audit.audit_entries" does not exist"`, т.к. Audit-подписчики уже писали в несуществующую таблицу при каждой мутирующей команде любого модуля; после миграции + `MigrateAllAsync`/`ResetAsync` полностью зелёный набор).

`dotnet build` всего решения — зелёный. `dotnet test tests/LupexWallet.UnitTests/...` — 146 тестов, зелёные. `dotnet test tests/LupexWallet.IntegrationTests/...` (реальный Docker/Postgres) — 59 тестов, зелёные.

## W3.1 — Angular UI: Reporting завершён (2026-09-11)

`frontend/src/app/features/reporting/` — `GET /reporting/total-amount` без/с `date` (UC-19/UC-21), `reporting.models.ts`/`reporting.service.ts` по образцу `balance-history`/`exchange-rates`. Маршрут `/reporting` в `app.routes.ts` переведён с заглушки `UnderConstructionComponent` на `ReportingComponent` (изменена только эта одна запись `loadComponent`, файл маршрутов больше не тронут).

- **Текущая сумма (UC-19)** — заметная карточка-виджет (`current-amount-card`, крупный шрифт `.amount-widget`): сумма + код валюты основного кошелька, отображаемые согласованно через `formatMoney(total.amount, currencyCode(total.amount.currencyId))` (код валюты подтягивается по `currencyId` через уже существующий `ReferenceDataService`, т.к. `MoneyResponse` бэкенда не заполняет `currencyCode` — только `amount`+`currencyId`).
- **Историческая сумма (UC-21)** — выбор даты → тот же запрос с `date`; `ratesAsOfDate` показывается пользователю ("Курс по состоянию на …") только когда фактическая дата курса отличается от запрошенной (для текущей суммы — от сегодняшней), а не всегда, чтобы не шуметь в обычном случае.
- **excludedWallets** — показывается как ненавязчивое предупреждение (`.warning`, тот же визуальный паттерн, что `sourceUnavailable` в `exchange-rates`), не как ошибка: "Кошельков, не учтённых в сумме: N" + список `<название> — <reason>`. Название кошелька резолвится по `walletId` через уже существующий `WalletsService.list({ includeArchived: true })` (без нового HTTP-метода) — если кошелёк не найден в списке, отображается сам id (сопоставление не усложняет задачу, оставлено полным, а не урезанным до id).
- **Отсутствие основного кошелька** — бэкенд отвечает 404 (пустое тело, не `problem+json`) — распознаётся явной проверкой `HttpErrorResponse.status === 404` (через `extractErrorMessage` тело было бы неотличимо от `fallback`, отдельная проверка статуса точнее сообщает пользователю: "Сумма не может быть посчитана: в системе не назначен основной кошелёк" вместо общего текста ошибки). Оба виджета (текущая/историческая сумма) обрабатывают этот случай независимо.

**Тесты**: `reporting.component.spec.ts` (4 теста, `HttpClientTestingModule`, по образцу `exchange-rates.component.spec.ts`) — загрузка и отображение текущей суммы; 404 → дружелюбное сообщение вместо сырой ошибки; excludedWallets резолвятся в имена кошельков; историческая сумма на дату + пометка отличающегося `ratesAsOfDate`. `ng build` — зелёный (новый lazy-чанк `reporting-component`, 8.55 kB). `ng test` — 103/103 зелёных (было 99, +4).

## W3.2 — Angular UI: Audit завершён (2026-09-11)

`frontend/src/app/features/audit/` — самостоятельный экран `/audit` (UC-25): выбор `entityType` из полного списка `docs/database/schema.md` (`Wallet`/`Operation`/`Transfer`/`BalanceSnapshot`/`WalletType`/`OperationType`/`Currency`/`ExchangeRateQuote`) + ручной ввод `entityId`, таблица истории (когда/действие/кто/что изменилось) с курсорной пагинацией "загрузить ещё" по образцу `features/balance-history`. Маршрут `/audit` в `app.routes.ts` переведён с заглушки `UnderConstructionComponent` на `AuditComponent` (изменена только эта одна запись `loadComponent`).

- **Единая точка HTTP-доступа** — `AuditService.list(entityType, entityId, options)` (`GET /audit-entries`, `core/api/cursor-page.ts` для курсора). Используется и самостоятельным экраном, и ссылками "История изменений".
- **Переиспользование без дублирования маршрута** — вместо отдельного пути для каждой сущности компонент реактивно читает query-параметры `entityType`/`entityId` (`ActivatedRoute.queryParamMap`) и сам подставляет их в форму + сразу выполняет поиск. Ссылки "История изменений" в `features/wallets/wallets.component.html` (кнопка в каждой строке таблицы кошельков) и `features/operations/operations.component.html` (в каждой строке операций, включая части переводов) — это обычные `routerLink="/audit"` с `[queryParams]`, без изменения бизнес-логики этих компонентов (только добавлен импорт `RouterLink` и одна ссылка в шаблоне/один столбец таблицы).
- **Пустая история** — отдельное состояние `hasSearched` отличает "ещё не искали" от "искали и ничего не нашли": во втором случае показывается "Изменений не найдено." текстом, а не как ошибка.
- **Diff и actor** — каждое изменённое поле показывается как `было → стало` (`—` для `null`); `actorKind = User` → "Пользователь", `System` → "Система (имя процесса)" или просто "Система", если имя процесса не заполнено.
- Backend (W2.2) не тронут; `app.routes.ts` — только одна заменённая запись `loadComponent` для `/audit`; `wallets.component.ts`/`operations.component.ts` — точечные правки (импорт `RouterLink` + одна ссылка), без переписывания существующей логики.

**Тесты**: `audit.service.spec.ts` (2 теста — параметры запроса, cursor/limit), `audit.component.spec.ts` (6 тестов — поиск по ручному выбору, явное пустое состояние вместо ошибки, "загрузить ещё" без дублей, автозаполнение и автопоиск по query-параметрам перехода из карточки, отображение actor "Система (процесс)", проблема-детали для 500). `ng build` — зелёный (новый lazy-чанк `audit-component`, 7.87 kB). `ng test` — 103/103 зелёных (13 файлов, включая новые `audit.*.spec.ts`).

## W3.3 — Angular UI: действия над кошельком завершён (2026-09-11)

`frontend/src/app/features/wallets/` (без правок `app.routes.ts` — маршрут `/wallets` уже существовал) — пять UC (UC-02…UC-06) поверх готового бэкенда W1.2, без нового компонента (расширен существующий `WalletsComponent`).

- **UC-02 (редактирование)** — отдельная форма `editForm` (`PATCH /wallets/{id}`): имя, тип (из активных `wallet-types`), описание, `includeInTotal`, порядок отображения, цвет, иконка. Поле `includeInTotal` задизейблено для основного кошелька с явной подсказкой (Q8: бэкенд тихо игнорирует выключение, не 409) — `getRawValue()` в `Reactive Forms` возвращает значение задизейбленного контрола, поэтому в PATCH всё равно уходит `true`.
- **UC-03 (архивирование)** и **UC-04 (удаление)** — общий механизм подтверждения без нативного `confirm()` (в проекте такого паттерна не было): клик по кнопке переводит строку таблицы в режим "Точно архивировать/удалить? [Да] [Отмена]" (`pendingConfirm` signal), реальный вызов — только по "Да". Кнопка "Архивировать" не показывается для уже архивных кошельков (backend не поддерживает разархивирование) — единственное условие сокрытия действия; остальные конфликты (основной кошелёк для архивирования/удаления, история для удаления) не прячутся превентивно, а показываются как читаемая ошибка из `problem-details.ts` после попытки — по аналогии с уже принятым в проекте паттерном (`features/reference-data`, деактивация/удаление используемого элемента).
- **UC-05 (назначение основным)** — кнопка скрыта только для уже текущего основного кошелька; 409 для архивного кошелька — читаемое сообщение.
- **UC-06 (смена валюты)** — отдельная маленькая форма (`currencyForm`, `PUT /wallets/{id}/currency`) с выбором новой валюты из активных `currencies`; 409 при наличии истории — читаемое сообщение.
- **Фильтр "показывать архивные"** — такого фильтра в UI ранее не было (список по умолчанию не показывал архивные, `WalletsService.list` уже поддерживал параметр `includeArchived`, но компонент его не использовал) — добавлен как чекбокс в тулбаре, дергающий `loadWallets({ includeArchived })`; архивные строки в таблице визуально приглушены (`.archived-row`) и получают собственный набор действий (без "Архивировать", наравне с остальными).
- **Параллельная правка W3.2 (Audit UI)** — на момент старта в `wallets.component.ts/html` уже была добавлена ссылка "История изменений" (`RouterLink` на `/audit` с query-параметрами). Ссылка сохранена и перенесена в общую ячейку "Действия" (было — отдельный безымянный столбец) без изменения её логики.

**Тесты**: `wallets.component.spec.ts` (новый файл, 10 тестов, `HttpClientTestingModule` + `provideRouter([])` для `RouterLink`) — включение архивных через фильтр (`includeArchived=true` в запросе), отсутствие кнопки "Архивировать" у архивного кошелька, дизейбл `includeInTotal` для основного при редактировании, успешный `PATCH` с перезагрузкой списка, 409 при архивировании основного, 409 при `set-primary` для архивного, 409 при смене валюты с историей, 409 и успешный сценарий удаления с подтверждением.

`ng test` — 113/113 зелёных (было 103, +10, новый файл спеков). `ng build` — зелёный, `wallets-component` — 22.57 kB (лениво подгружаемый чанк).

## Тест на регистрацию кросс-модульных портов ADR-0009 (M6 из код-ревью волны 4, 2026-09-11)

Код-ревью нашло, что fail-fast требование ADR-0009 («пустой список реализаций — ошибка конфигурации, а не молчаливый `false`») было реализовано только частично: проверка `Count == 0` в момент вызова (`Wallets.Application/IWalletHistorySource.cs`, `ReferenceData.Application/IReferenceItemUsageProbe.cs`) не ловит потерю ОДНОЙ из нескольких зарегистрированных реализаций — список остаётся непустым, исключения не будет, агрегация тихо становится неполной.

Добавлен `tests/LupexWallet.IntegrationTests/Infrastructure/CrossModuleContractsRegistrationTests.cs` (2 теста, по образцу `ApiTestFixture`/`Factory.Services`, резолв через `CreateScope()` — сервисы `Scoped`): `IWalletHistorySource` — ровно 2 регистрации (`Operations`, `BalanceHistory`), `IReferenceItemUsageProbe` — ровно 3 регистрации (`Wallets`, `Operations`, `ExchangeRates`). Других `IEnumerable<TPort>`-контрактов в проекте не найдено (ExchangeRates/Reporting используют только 1:1 lookup-порты) — третья проверка не потребовалась. `dotnet test tests/LupexWallet.IntegrationTests/...` — 78/78 зелёных (было 76, +2).

## UC-13 — смена кошелька операции при редактировании (решение пользователя, 2026-09-11)

Ux-parity-ревью нашло молчаливое расхождение: UC-13 (`docs/requirements/user-scenarios.md`) явно допускает менять кошелек операции при редактировании, но `PATCH /operations/{id}` не принимал `walletId`. Пользователь явно решил: добавить `walletId` в `OperationUpdateRequest` и реализовать перенос операции между кошельками с пересчетом баланса обоих.

- **`docs/api/openapi.yaml`** (только раздел `/operations`) — `OperationUpdateRequest.walletId` (опционально по схеме, как и остальные поля этой схемы — конвенция проекта для `*UpdateRequest`, см. `WalletUpdateRequest`; фактически всегда передаётся Angular-формой, т.к. форма — полный объект).
- **`Operations.Domain.Operation`** — новый метод `MoveToWallet(newWalletId, ...)`: перенос операции на другой кошелек трактуется как удаление со старого + создание на новом. В отличие от `Update`, не бросает `OperationCurrencyImmutableException` — валюта операции меняется вслед за валютой нового кошелька (та же логика, что при первичном `Create`). `Update` и `MoveToWallet` переиспользуют общий приватный `ApplyUpdate`, поднимающий `OperationUpdated` с новым полем `PreviousWalletId` (равно текущему `WalletId`, если кошелек не менялся).
- **`UpdateOperationCommandHandler`** — ветвление по `newWalletId == operation.WalletId`: без смены кошелька — прежняя логика (инкрементная дельта на одном кошельке); со сменой — реверс `AppliedDelta` на старом кошельке (`ApplyDeltaAsync(oldWalletId, oldDelta.Negate())`) + `intendedDelta`, посчитанный от `CurrentBalance` НОВОГО кошелька (как при `Create`), применённый на новом. Дата операции валидируется против `AccountingStartDate` нового кошелька.
- **`BalanceHistory.Infrastructure.OperationUpdatedHandler`** — при `PreviousWalletId != WalletId` пересчитывает оба кошелька независимо (`IWalletOperationsLookup` фильтрует по `WalletId`, операция теперь принадлежит только новому): старый — с `PreviousOperationDate`, новый — с `OperationDate`.
- **Audit** — изменений не потребовалось: `AuditRecorder.RecordFromChangeTrackerAsync` берёт diff из EF Core `ChangeTracker` универсально по всем изменившимся свойствам (`WalletId`/`CurrencyId` в том числе), не нуждается в точечном перечислении полей.
- **Ограничение сохранено**: операция — часть перевода (`TransferId != null`) — по-прежнему нельзя перенести напрямую через `/operations` (`OperationPartOfTransferException`, 409), включая попытку сменить только `walletId`.
- **Angular** (`features/operations/operation-form`) — поле кошелька разблокировано в режиме редактирования (было задизейблено с подсказкой «нельзя изменить», подсказка убрана); `UpdateOperationRequest.walletId` — теперь обязательное поле, всегда отправляется формой.

**Тесты**: `tests/LupexWallet.UnitTests/Operations/OperationTests.cs` (+5: `MoveToWallet` — часть перевода/смена валюты/событие с `PreviousWalletId`, `Update` — `PreviousWalletId` не меняется при том же кошельке), новый `tests/LupexWallet.UnitTests/Operations/UpdateOperationCommandHandlerTests.cs` (7 тестов — тот же кошелек/перенос/часть перевода/кошелек не найден/смена валюты/дата вне диапазона нового кошелька), `tests/LupexWallet.IntegrationTests/Operations/OperationsApiTests.cs` (+1 сквозной: перенос операции, баланс обоих кошельков), `tests/LupexWallet.IntegrationTests/BalanceHistory/BalanceHistoryApiTests.cs` (+1: каскадный пересчет истории обоих кошельков), `tests/LupexWallet.IntegrationTests/Operations/TransfersApiTests.cs` (+1: перенос операции — части перевода → 409); существующие PATCH-тесты (`OperationsApiTests`, `BalanceHistoryApiTests`, `TransfersApiTests`) обновлены — теперь передают `walletId` явно (стало обязательным полем DTO). `frontend/.../operation-form.component.spec.ts` обновлён (поле не задизейблено) + новый тест на отправку нового `walletId`.

`dotnet build`/`dotnet test` (backend, Unit+IntegrationTests Operations/BalanceHistory) — зелёные (188 unit, 29 integration в затронутых модулях). Полный прогон `IntegrationTests` также показывает 2 незатронутых падения в `Audit.AuditApiTests` (пагинация `GET /audit-entries`, 500) — код Audit-модуля на момент проверки не закоммичен и не редактировался в рамках этой задачи, вне её объёма. `ng build`/`ng test` (frontend) — зелёные (114/114, 14 файлов).

## Находки code-quality-reviewer (волна 4), исправленные (2026-09-11)

Вердикт прошлого код-ревью — "Request changes" по ExchangeRates/Reporting/Audit/Wallets lifecycle/кросс-модульным пробникам. Все пункты устранены, `dotnet build` — зелёный, `dotnet test` — 188 unit + 83 integration зелёных (было 177+75, включая падавшие `Audit.AuditApiTests` из предыдущей записи выше — теперь исправлены как C1/M1 ниже).

- **C1** — `ToString("G29", CultureInfo.InvariantCulture)` переключается на научную нотацию для значений < 0.0001 (`0.00001m` → `"1E-05"`), нарушая контракт `Money`/`ExchangeRateQuote` (`openapi.yaml`, `string`/`format: decimal`) и фронтендовый `AMOUNT_PATTERN`. Заменено на `ToString(CultureInfo.InvariantCulture)` (дефолтное форматирование `decimal` не переходит в научную нотацию) во всех 7 местах: `ExchangeRatesEndpointsExtensions`, `ReportingEndpointsExtensions`, `WalletsEndpointsExtensions` (×2), `BalanceHistoryEndpointsExtensions`, `OperationsEndpointsExtensions` (×2). Регресс-тест — `ExchangeRatesApiTests.Refresh_VerySmallRate_FormatsAsPlainDecimalWithoutScientificNotation`.
- **C2** — `RefreshExchangeRatesCommand` реализовывал `ICommand<T>`, поэтому `TransactionBehavior` оборачивал весь обработчик (последовательные HTTP-вызовы к Frankfurter по каждой паре) в `TransactionScope` с дефолтным таймаутом 60с — при 2+ недоступных парах транзакция аварийно завершалась по таймауту (500 вместо предписанного 502) и откатывала уже полученные курсы. Убран `ICommand<T>` у команды (оставлен только `IRequest<T>`) — обработчик и так управляет атомарностью сам через `IExchangeRatesUnitOfWork.SaveChangesAsync`, ambient-транзакция ему не нужна (тот же принцип, что и ADR-0011). Проверено на `Refresh_FrankfurterUnavailable_...`.
- **M1** — `Audit.Domain.MonotonicClock` даёт строго возрастающие метки только на 100нс-тиках CLR-процесса; колонка `occurred_at` (PostgreSQL `timestamptz`) хранит только микросекунды — курсорная пагинация `GET /audit-entries` по одному `OccurredAt` могла терять записи при коллизии после округления. Курсор `AuditEntryCursor` расширен до пары `(OccurredAt, Id)`. `Guid` не имеет операторов сравнения в CLR и не транслируется EF Core/Npgsql в SQL `<` (`CompareTo` в LINQ-предикате даёт `InvalidOperationException`/500 — обнаружено при первой попытке фикса) — решение: `WHERE occurred_at < cursor` выполняется в БД (дёшево, переводимо), а тай-брейк по `Id` для узкого набора строк с точно тем же `occurred_at` — в памяти после материализации (набор коллизий всегда мал, полная история сущности не читается). Регресс-тест — `AuditApiTests.GetAuditEntries_OccurredAtCollision_ReturnsAllEntriesWithoutLoss` (коллизия воспроизведена напрямую через `AuditDbContext`, в обход `MonotonicClock`, чтобы не зависеть от таймингов).
- **M2** — В миграции `ExchangeRates` отсутствовали `CHECK (rate > 0)` для `latest_exchange_rates`/`historical_exchange_rates` (расхождение со `schema.md`). Добавлены `ck_latest_rates_positive`/`ck_historical_rates_positive` в `ExchangeRateQuoteConfiguration`/`HistoricalExchangeRateConfiguration`. Миграция `InitialCreate` модуля ExchangeRates на момент правки была не закоммичена в git (весь модуль — часть текущей незакоммиченной работы), поэтому перегенерирована (`dotnet ef migrations remove` + `add InitialCreate`) вместо добавления отдельной миграции — по аналогии с тем, что остальные модули проекта тоже имеют ровно одну `InitialCreate` без последующих миграций.
- **M3** — `IWalletHistorySource`/`IReferenceItemUsageProbe` агрегировали несколько реализаций через `Task.WhenAll` — при вызове внутри ambient-`TransactionScope` несколько источников конкурировали бы за один физический connection (Npgsql не поддерживает параллельные команды на одном соединении в транзакции). Заменено на последовательный `foreach` с коротким замыканием на первом `true` в `WalletHistorySourceExtensions.AnyHasHistoryAsync`/`ReferenceItemUsageProbeExtensions.AnyIsUsedAsync`.
- **M4** — `GET /reporting/total-amount` мог вернуть 404 (нет основного кошелька) — штатное, но не задокументированное поведение. Добавлен `404` в `openapi.yaml` (раздел `/reporting`).
- **M5** — `GET /exchange-rates/latest` мог отдавать устаревшие пары к прежней основной валюте после смены основного кошелька (`RefreshExchangeRatesCommandHandler` только добавлял новые пары, никогда не удалял старые; читающий запрос не фильтровал по текущей основной валюте). Исправлено с обеих сторон: `RefreshExchangeRatesCommandHandler` теперь удаляет пары к валютам, переставшим быть основной, при каждом обновлении; `GetLatestExchangeRatesQueryHandler` дополнительно фильтрует выдачу по текущей `IWalletCurrencySet.PrimaryCurrencyId` (страховка на окно между сменой основного кошелька и фоновым пересчётом, ADR-0011) — потребовалась новая ссылка `ExchangeRates.Application` → `Wallets.Application` (по аналогии с уже существующей `BalanceHistory.Application` → `Wallets.Application`). Регресс-тест — `ExchangeRatesApiTests.GetLatest_AfterPrimaryWalletChanged_DoesNotReturnRatesToPreviousPrimaryCurrency`.
- **M9** — `WalletUpdateRequest` в `openapi.yaml` не помечал поля как `required`, хотя `WalletContracts.cs` требует полный объект (не partial merge). Добавлено `required: [name, walletTypeId, includeInTotal, displayOrder]`.

`docs/api/openapi.yaml` отредактирован точечно (разделы `/reporting`, `/exchange-rates`, `/wallets`), не переформатирован целиком — параллельно с этой правкой шла отдельная правка раздела `/operations` (UC-13, см. запись выше). `redocly lint` — валиден (1 предсуществующий warning по `info.license`, не связан с этой правкой).

## Найден и исправлен критичный баг: реальный хост никогда не применял EF Core-миграции (2026-09-11)

Обнаружено пользователем при первом запуске приложения после завершения конвейера ("похоже миграции не накатываются"). Причина: `src/Host/LupexWallet.Api/Program.cs` никогда не вызывал `Database.MigrateAsync()` ни для одного модуля — только тестовая инфраструктура (`tests/LupexWallet.IntegrationTests/Infrastructure/LupexWalletApiFactory.MigrateAllAsync()`) применяла миграции явно, поэтому весь прогон тестов (188 unit + 83 integration) оставался зелёным, а реальный `dotnet run` против чистой БД падал бы на первом запросе с `relation ... does not exist` — баг присутствовал с самого начала проекта (до этого прогона конвейера), просто никогда не проявлялся, т.к. проверки "вживую" в предыдущих срезах либо переиспользовали уже смигрированную БД от предыдущего ручного теста, либо этот шаг был пропущен.

**Исправлено:** в `Program.cs`, сразу после `var app = builder.Build();`, добавлен блок, применяющий `Database.MigrateAsync()` для всех шести `DbContext` (Wallets, ReferenceData, Operations, BalanceHistory, ExchangeRates, Audit) в одном scope перед стартом хоста. `Reporting` не имеет своего `DbContext` (§3 high-level-architecture.md), миграция не нужна.

Проверено вживую: чистый `docker compose up -d` (пустая схема `public`) → `dotnet run --project src/Host/LupexWallet.Api` → все 6 схем (`wallets`, `reference_data`, `operations`, `balance_history`, `exchange_rates`, `audit`) созданы автоматически, включая `CREATE RULE audit_entries_no_update/no_delete`, Swagger отвечает 200, фоновые задачи (`BalanceSnapshotSchedulerHostedService`, `ExchangeRateRefreshBackgroundService`) стартуют без ошибок на пустой БД. `dotnet test` (оба проекта) — по-прежнему зелёные (188 unit + 83 integration), повторное применение уже применённых миграций через `WebApplicationFactory` идемпотентно, конфликта с `LupexWalletApiFactory.MigrateAllAsync()` нет.

**Урок для будущих прогонов:** ручная/сквозная проверка "вживую" в рамках среза должна включать хотя бы один раз старт реального `dotnet run` против **чистой** (только что поднятой) БД, а не полагаться только на зелёные интеграционные тесты — они используют собственный механизм применения миграций, который может маскировать отсутствие эквивалентного механизма в реальном хосте.
