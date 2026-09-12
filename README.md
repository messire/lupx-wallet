# LupexWallet

Однопользовательское приложение для учета личных финансов. Стек: Angular (frontend), ASP.NET Core / EF Core / PostgreSQL (backend, модульный монолит).

## Структура репозитория

- [docs/](docs/) — требования, архитектура (DDD-модель, ADR), схема БД, OpenAPI-контракт
- [backend/](backend/) — .NET backend, инструкции запуска и тестов — [backend/README.md](backend/README.md)
- [frontend/](frontend/) — Angular SPA, инструкции запуска и тестов — [frontend/README.md](frontend/README.md)

## Быстрый старт

```bash
docker compose up -d                              # PostgreSQL для локальной разработки
dotnet run --project backend/src/Host/LupexWallet.Api   # backend, http://localhost:5199
cd frontend && npm install && npm start           # frontend, http://localhost:4200
```

Подробности (аутентификация для разработки, тесты, переменные окружения) — в [backend/README.md](backend/README.md) и [frontend/README.md](frontend/README.md).

## Текущий статус реализации

Актуальный статус по срезам, известные упрощения и инфраструктурный TODO — в [docs/PROGRESS.md](docs/PROGRESS.md) (единственный источник правды по прогрессу, этот README не дублирует таблицу).
