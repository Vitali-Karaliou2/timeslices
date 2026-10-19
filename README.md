# timeslices
An educational app where the history of software development is shown as a collection of snapshots, each featuring a set of working examples that demonstrate the solution to a problem 'before' and 'after' a new approach appeared, with visualization of both the result and the process; the goal is to train the ability to judge other people's code.

---

# TimeSlices — Временны́е срезы

Обучающее приложение: история разработки ПО как частично упорядоченная коллекция
временны́х срезов с работающими примерами «до/после». См. `docs/brief/` и `CLAUDE.md`.

Стадия 0→1. Сборка: `dotnet build`; тесты: `dotnet test`; запуск: `dotnet run --project src/TimeSlices.Web`.

## Запуск для проверки (Development)
1. .NET 10 SDK (LTS). В облачной сессии его ставит `scripts/install_dotnet.sh`
   (запасной путь — пакет `dotnet-sdk-10.0` из Ubuntu).
2. `dotnet test TimeSlices.sln`, затем `dotnet run --project src/TimeSlices.Web` → http://localhost:5000.
   На Windows то же двойным щелчком: `bat\run-tests.bat` (все тесты) и `bat\run-app.bat` (приложение, браузер откроется сам).
   База данных (SQLite, `app.db`) — только просмотр: `bat\db-show.bat` (таблицы), `bat\db-show.bat <таблица> [N]`, `bat\db-show.bat --sql "select ..."`.
   БД `app.db` создаётся миграциями при старте рядом с проектом (в .gitignore).
3. Dev-пользователи (из `src/TimeSlices.Web/appsettings.Development.json`):
   `learner@timeslices.local` / `Learner-dev-1`, `examiner@timeslices.local` / `Examiner-dev-1`,
   `admin@timeslices.local` / `Admin-dev-1`.
