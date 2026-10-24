# timeslices
An educational app where the history of software development is shown as a collection of snapshots, each featuring a set of working examples that demonstrate the solution to a problem 'before' and 'after' a new approach appeared, with visualization of both the result and the process; the goal is to train the ability to judge other people's code.

---

# TimeSlices — Временны́е срезы

Обучающее приложение: история разработки ПО как частично упорядоченная коллекция
временны́х срезов с работающими примерами «до/после». См. `docs/brief/` и `CLAUDE.md`.

Стадия 0→1: один человек плюс модель. Первый набор — десять срезов (ADR 0003 и 0014 в `docs/decisions/`);
продвинутый срез ENIAC — на этапе 1→n. Порядок срезов задают рёбра графа, а не годы.

Сборка: `dotnet build TimeSlices.sln`; тесты: `dotnet test TimeSlices.sln`;
запуск: `dotnet run --project src/TimeSlices.Web` → http://localhost:5000.
Миграции: `dotnet ef migrations add <Name> --project src/TimeSlices.Web`.
Проверка содержания: `python3 scripts/validate_all.py` (на Windows — `python`).

## Запуск для проверки (Development)
1. .NET 10 SDK (LTS) и Python 3 (нужен тестам валидатора). В облачной сессии .NET ставит `scripts/install_dotnet.sh`
   (запасной путь — пакет `dotnet-sdk-10.0` из Ubuntu).
2. `dotnet test TimeSlices.sln`, затем `dotnet run --project src/TimeSlices.Web` → http://localhost:5000.
   На Windows то же двойным щелчком: `bat\run-tests.bat` (все тесты) и `bat\run-app.bat` (приложение, браузер откроется сам).
   База данных (SQLite, `app.db`) — только просмотр: `bat\db-show.bat` (таблицы), `bat\db-show.bat <таблица> [N]`, `bat\db-show.bat --sql "select ..."`.
   Одна таблица двойным щелчком: имя — в строке `set TABLE=...` файла `bat\db-show-table.bat`.
   БД SQLite `app.db` создаётся миграциями при старте в рабочей папке приложения: при `dotnet run --project src/TimeSlices.Web`
   это `src/TimeSlices.Web/app.db` (в .gitignore). Чтобы начать с чистой базы — остановить приложение и удалить файл.
3. Dev-пользователи (из `src/TimeSlices.Web/appsettings.Development.json`):
   `learner@timeslices.local` / `Learner-dev-1`, `examiner@timeslices.local` / `Examiner-dev-1`,
   `admin@timeslices.local` / `Admin-dev-1`.
