# Конвенция: структура репозитория

Короткая справка. Обоснование и целевые деревья — в [ADR-0012](../architecture/adr/0012-repository-structure-and-conventions.md).

## Куда положить файл

| Что | Где |
|---|---|
| Backend-код | `backend/src/` — см. [backend.md](backend.md) |
| Backend-тесты | `backend/tests/LupexWallet.UnitTests` / `LupexWallet.IntegrationTests` |
| Frontend-код | `frontend/src/app/` — см. [frontend.md](frontend.md) |
| Требования, архитектура, ADR, схема БД, OpenAPI | `docs/` |
| Прогресс и план работ | `docs/PROGRESS.md` (единственный источник правды) |

## Правила

- Backend и frontend — равноправные top-level папки. Каждая — самодостаточная (свой `README.md`, свои настройки сборки); корневой `README.md` — точка входа со ссылками.
- Не создавать неопределённые папки `Helpers`, `Utils`, `Common`, `Misc`. Использовать предметные имена (`Money`, `Pagination`, `Authentication`).
- Пустые папки шаблона не создаются — только под реально существующие файлы.
- Doc-комментарии в коде (XML `<summary>`/`<param>` в C#, JSDoc в TypeScript) — только на английском и сжатые: что делает + краткое назначение параметров, без истории изменений и без очевидных пояснений. Комментарий пишется только там, где неочевидна причина (ограничение, инвариант, обход бага).
- Архитектурные решения — новым ADR в `docs/architecture/adr/`. Прогресс и договорённости — в `docs/PROGRESS.md`. Исторические записи не переписывать как текущее состояние.
