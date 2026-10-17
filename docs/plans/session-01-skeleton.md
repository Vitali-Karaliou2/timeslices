# Сессия 01. Скелет оболочки

Ветка: `session-01-skeleton`. Ожидаемый результат — PR в `main`, который можно собрать,
запустить и прогнать тесты в облачной сессии и на локальной машине.

## Задача
Создать скелет ASP.NET Core приложения-оболочки по CLAUDE.md и docs/spec/shell-spec.md.
Никакого содержания коллекции, кроме одного фиктивного среза `cpu-cycle` и одного
`static-page-1995` с пустыми примерами — ровно столько, чтобы работала навигация по графу.

## Шаги
1. `dotnet new sln -n TimeSlices`; `dotnet new razor -n TimeSlices.Web -o src/TimeSlices.Web --auth Individual`
   (Identity + SQLite из шаблона); `dotnet new xunit -n TimeSlices.Tests -o tests/TimeSlices.Tests`;
   добавить проекты в решение; `.gitignore` для dotnet (`dotnet new gitignore`).
2. Роли `Learner`, `Examiner`, `Admin`; сидирование трёх dev-пользователей при старте
   в Development (логины и пароли — в `appsettings.Development.json`, не в коде).
3. Слой содержания: `ContentLoader`, который читает `Content/tracks.json` и `Content/slices/*/slice.json`,
   строит DAG, проверяет отсутствие циклов при старте (падать с понятной ошибкой).
4. Сущности EF: `UserSliceState` (пользователь × тематический срез × состояние из brief 5.3,
   пока только три состояния: Unavailable, Available, InProgress, Done), `Attempt` — пока не нужен.
5. Страницы: `/` (главная: треки и срезы с состояниями для текущего пользователя),
   `/slices/{id}` (страница среза: поля brief 7.1, список примеров, кнопки «Готово» и «Далее»),
   `/slices/{id}/examples/{exampleId}` (пример: iframe на `entry` выбранной реализации,
   выпадающий список реализаций, код для показа, процесс — пока только список кадров).
6. Тесты (xUnit) по docs/acceptance.md: граф без циклов; срез недоступен, пока не пройдены
   предшественники; «Готово» переводит в Done и открывает следующий; повторное прохождение
   без сброса не ломает последующие.
7. `dotnet build`, `dotnet test` — зелёные. Коммит, пуш, PR. Отчёт по скиллу `session-report`.

## Не делать в этой сессии
Контроль знаний, экзаменатор, эмуляция, процессы с проигрыванием кадров, оформление.
