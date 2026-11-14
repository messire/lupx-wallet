# ADR-0007: Структура репозитория и конвенции размещения файлов

## Статус

Принято — 2026-09-12. Пересобрано 2026-09-30.

**Состояние реализации:** реализовано 2026-09-12 (структурный рефакторинг завершён, сборки и тесты зелёные, поведение не менялось). Правила зависимостей проверяются в CI. Отклонение: модуль `Reporting` не приведён к шаблону §2 — файлы лежат в корнях `Application/` и `Api/`.

## Контекст

Пользователю было трудно ориентироваться в коде: 30 backend-проектов с путями вида `Modules/Wallets/Wallets.Application/LupexWallet.Wallets.Application.csproj`, смешение сценариев, интерфейсов, политик, работы с БД и регистрации зависимостей в корнях слоёв; frontend-страницы импортировали HTTP-сервисы из соседних features; комментарии содержали историю правок вместо описания устройства. Задача первого этапа — предсказуемое размещение файлов без изменения поведения.

## Решение

### 1. Явные границы backend и frontend

```text
lupex-wallet/
├── backend/
│   ├── src/
│   │   ├── Host/LupexWallet.Api/
│   │   ├── Modules/{Wallets,Operations,ReferenceData,BalanceHistory,ExchangeRates,Reporting,Audit}/
│   │   └── BuildingBlocks/{SharedKernel,Infrastructure}/
│   ├── tests/{LupexWallet.UnitTests,LupexWallet.IntegrationTests}/
│   ├── scripts/                 — проверка архитектуры
│   ├── LupexWallet.sln, Directory.Build.props, Directory.Packages.props, global.json
│   ├── Dockerfile
│   └── README.md
├── frontend/
│   ├── src/, scripts/, e2e/
│   ├── package.json, vercel.json
│   └── README.md
├── docs/{architecture,conventions,requirements,api,database,design}/, docs/PROGRESS.md
├── .github/workflows/
├── docker-compose.yml
└── README.md
```

Backend и frontend — самостоятельные сборки и деплои со своими инструкциями запуска. Корневой `README.md` — короткая точка входа со ссылками. `docker-compose.yml` в корне — локальное окружение.

### 2. Единый шаблон backend-модуля

```text
Modules/Wallets/
├── Domain/          — агрегаты, Events/, Exceptions/
├── Application/     — UseCases/<Action>/, Abstractions/{Persistence,Integration}/, Models/, Policies/
├── Infrastructure/  — Persistence/{DbContext, Configurations, Repositories, Migrations}/, Integration/,
│                      BackgroundJobs/, ExternalServices/, <Module>ModuleExtensions.cs
└── Api/             — Contracts/, Endpoints/
```

| Назначение | Расположение внутри модуля |
|---|---|
| Пользовательский сценарий | `Application/UseCases/<Action>/` (команда и обработчик могут быть в одном файле) |
| Правила сущности | `Domain/` |
| Интерфейсы хранения | `Application/Abstractions/Persistence/` |
| Интерфейсы взаимодействия модулей | `Application/Abstractions/Integration/` |
| Общие для сценариев модели и политики | `Application/Models/`, `Application/Policies/` |
| EF Core, репозитории, миграции | `Infrastructure/Persistence/` |
| Межмодульные адаптеры | `Infrastructure/Integration/` |
| Фоновые задачи | `Infrastructure/BackgroundJobs/` |
| Внешние HTTP-клиенты | `Infrastructure/ExternalServices/` |
| HTTP-маршруты и контракты | `Api/Endpoints/`, `Api/Contracts/` |

Полные имена проектов и сборок сохранены; названия папок слоёв сокращены. Пустые папки ради шаблона не создаются; у `Reporting` нет `Domain`. Перемещение интерфейсов не меняет их владельца и направление зависимостей ([ADR-0001](./0001-modular-monolith.md), §2).

### 3. Frontend: экраны отдельно от доступа к данным

```text
frontend/src/app/
├── core/{auth,http}/                 — авторизация и общая инфраструктура
├── data-access/<domain>/             — <domain>-api.service.ts, <domain>-api.models.ts
├── features/<domain>/pages/…, components/…
├── shared/{ui,money,pagination,…}/   — переиспользуемое без знания об экранах
├── layout/shell/
├── app.routes.ts, app.config.ts
```

- `pages` — компоненты маршрутов; `components` — компоненты конкретной feature.
- `data-access` — HTTP-клиенты и модели API по предметным областям; модели состояния экрана остаются в feature.
- Feature не импортирует другую feature. `core`, `shared`, `data-access` не зависят от features. Граф импортов без циклов.
- Компонент, шаблон, стили и тест лежат рядом.

### 4. Короткие проверяемые конвенции

Справочники: `docs/conventions/repository-structure.md`, `backend.md`, `frontend.md`.

- Не создавать папки `Helpers`, `Utils`, `Common`, `Misc`; использовать предметные имена.
- C# — PascalCase для папок и файлов типов; frontend — kebab-case с явным назначением (`*-api.service.ts`, `*-page.component.ts`).
- Doc-комментарии (XML `<summary>` в C#, JSDoc в TypeScript) — только на английском и сжатые: что делает и назначение параметров, без истории изменений и пересказа реализации. Комментарий пишется там, где неочевидна причина (ограничение, инвариант, обход бага). Содержательные ссылки на ADR сохраняются.
- Общий код выделяется по реальному использованию; локальный остаётся у владельца.
- Backend-тесты — в двух существующих тестовых проектах с группировкой по модулям; frontend-тесты — рядом с проверяемыми файлами.
- История изменений — в `docs/PROGRESS.md`, причины архитектурных решений — в ADR.
- Правила зависимостей закреплены автоматическими проверками в CI: `backend/scripts/check-architecture.mjs` (границы модулей и слоёв по ADR-0001, шаблон §2) и `frontend/scripts/check-feature-boundaries.mjs` (§3). Проверки учитывают принятые ADR, а не вводят новые ограничения. Зависимость `Domain` от `Infrastructure` исключена самими `ProjectReference`.

## Последствия

- Одинаковое размещение сокращает поиск; отдельные корни backend/frontend соответствуют независимым сборкам и деплоям.
- Доступ к данным не принадлежит экрану, который первым его использовал; переход от экрана к HTTP-клиенту требует открыть другой каталог.
- 30 backend-проектов сохранены: объединение четырёх проектов модуля в один убрало бы часть компиляционных ограничений слоёв.

## Отклонённые варианты

- **Объединение проектов модуля** — теряется компиляционная граница слоёв.
- **Полная перепись под другую архитектуру** — не решает задачу этапа и увеличивает риск изменения поведения.

## Связанные материалы

- [ADR-0001](./0001-modular-monolith.md) — границы модулей.
- [high-level-architecture.md](../high-level-architecture.md).
- [docs/PROGRESS.md](../../PROGRESS.md), «ПЛАН: структурный рефакторинг (2026-09-12)».
