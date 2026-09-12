# LupexWallet

Однопользовательское приложение для учета личных финансов. Стек: Angular (frontend), ASP.NET Core / EF Core / PostgreSQL (backend, модульный монолит).

## Структура репозитория

- [docs/](docs/) — требования, архитектура (DDD-модель, ADR), схема БД, OpenAPI-контракт
- [backend/](backend/) — .NET backend, инструкции запуска и тестов — [backend/README.md](backend/README.md)
- [frontend/](frontend/) — Angular SPA, инструкции запуска и тестов — [frontend/README.md](frontend/README.md)

## Быстрый старт

```bash
docker compose up -d                              # PostgreSQL для локальной разработки
dotnet run --project backend/src/Host/LupexWallet.Api   # backend, http://localhost:5086
cd frontend && npm install && npm start           # frontend, http://localhost:4200
```

Подробности (аутентификация для разработки, тесты, переменные окружения) — в [backend/README.md](backend/README.md) и [frontend/README.md](frontend/README.md).

## Независимый запуск backend/frontend в Docker

Помимо `docker-compose.yml` (только PostgreSQL), в корне есть `docker-compose.backend.yml` и
`docker-compose.frontend.yml` — по одному сервису в каждом, чтобы поднимать backend и frontend
в Docker независимо друг от друга и в любой комбинации, но чтобы вместе они работали как единый
стенд. Каждый файл запускается сам по себе (в т.ч. отдельным run-конфигом IDE на каждый файл) —
явной зависимости между сервисами через `depends_on` нет, т.к. они не обязаны подниматься одной
командой: backend резолвит `postgres` по имени контейнера через общую внешнюю сеть
`docker-network-shared`, если тот уже поднят (любым из способов — `docker compose up` из корня,
отдельным run-конфигом IDE и т.д.), независимо от того, в рамках какого именно `docker compose`
вызова.

Что отлаживаем в IDE → что поднять в Docker:

```bash
# Всё в Docker (ничего не отлаживается в IDE) — одной командой или тремя отдельными
docker compose -f docker-compose.yml up -d
docker compose -f docker-compose.backend.yml up -d --build
docker compose -f docker-compose.frontend.yml up -d --build

# Отлаживаю только frontend в IDE → postgres + backend в Docker
docker compose -f docker-compose.yml up -d
docker compose -f docker-compose.backend.yml up -d --build

# Отлаживаю только backend в IDE → postgres + frontend в Docker
docker compose -f docker-compose.yml up -d
docker compose -f docker-compose.frontend.yml up -d --build
```

Postgres должен быть поднят раньше backend'а — Docker Compose не знает об этом порядке между
отдельными вызовами, поэтому при первом старте стенда стоит подождать пару секунд между командами
(или просто перезапустить `docker-compose.backend.yml`, если backend упал с ошибкой подключения).

Backend в Docker слушает тот же `localhost:5086`, что и при запуске из IDE, поэтому frontend
(из Docker или из IDE) обращается к нему одинаково независимо от того, где backend поднят.

## Текущий статус реализации

Актуальный статус по срезам, известные упрощения и инфраструктурный TODO — в [docs/PROGRESS.md](docs/PROGRESS.md) (единственный источник правды по прогрессу, этот README не дублирует таблицу).
