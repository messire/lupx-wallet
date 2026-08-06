# Прогресс проекта

Статус на 2026-08-06. Обновляется по мере продвижения — при завершении каждого этапа/среза дополните таблицу и раздел "Следующий шаг" ниже.

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

## Следующий шаг

**ReferenceData** (типы кошельков, типы операций, валюты, системный `OperationBehaviorKind`) — разблокирует нормальный выбор `walletTypeId`/`currencyId` в форме создания кошелька вместо ручного ввода UUID (текущее временное упрощение, см. ниже).

После ReferenceData — по порядку модулей: Operations (+ Transfers), BalanceHistory, ExchangeRates, Audit, Reporting.

## Известные упрощения / TODO

- **`walletTypeId`/`currencyId` в форме создания кошелька** — обычные текстовые поля с UUID, без выпадающего списка. Снимется модулем ReferenceData.
- **Кросс-модульная валидация ссылок** — `Wallets` не проверяет, что переданные `walletTypeId`/`currencyId` реально существуют в `ReferenceData` (там пока нет данных). Появится вместе с публикацией read-контрактов ReferenceData (`ddd-model.md`, §1 карта контекста).
- **Аудит и BalanceHistory** — модули-заглушки (`AddXxxModule`/`MapXxxEndpoints` пустые), доменные события Wallets (`WalletCreated`, `PrimaryWalletChanged` и т.д.) публикуются, но подписчиков пока нет.
- **Точная граница "связанности" при удалении Operation** (ADR-0002) — реализуется вместе с модулем Operations.

## Локальное окружение для разработки

- Backend: `dotnet run` в `src/Host/LupexWallet.Api` (по умолчанию порт зависит от `--urls`/launchSettings; в текущей сессии тестировалось на `:5199`).
- Frontend: `npm start` в `frontend/` (Angular dev server, порт 4200).
- PostgreSQL: Docker-контейнер `lupex-wallet-postgres` (`postgres:16`, БД `lupex_wallet`, пользователь/пароль `postgres`/`postgres`, порт 5432) — поднят вручную для тестирования, не входит в `docker-compose`/CI, его нужно будет формализовать (см. TODO ниже).
- Пароль для входа в приложение при локальной разработке: `ChangeMe123!` (хеш в `appsettings.Development.json`, не для production).
- Строка подключения к БД для локальной разработки — в `appsettings.Development.json` (`ConnectionStrings:LupexWallet`); в production обязательна переменная окружения `ConnectionStrings__LupexWallet` (приложение падает при старте, если её нет).

## Инфраструктурный TODO (не забыть)

- Формализовать поднятие PostgreSQL для разработки — `docker-compose.yml` в репозитории вместо разового `docker run`, выполненного вручную в этой сессии.
- CI/CD пока не настроен — при появлении первого реального деплоя потребуется отдельный этап.
