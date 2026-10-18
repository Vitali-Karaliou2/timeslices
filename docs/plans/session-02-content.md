# Сессия 02. Состав коллекции: треки и семь срезов

Ветка: `session-02-content`. Ожидаемый результат — PR в `main`: на главной видны четыре трека
и семь срезов первого набора в статусе `draft`, граф без циклов, межтрековые рёбра работают,
`dotnet test` зелёный.

Перед началом прочитай: CLAUDE.md, `docs/decisions/0003-first-set-composition.md`,
`docs/spec/manifest-schema.md`, описания срезов в `docs/slices/` (формат —
`docs/spec/slice-description-format.md`).

## Что уже решено автором (не переспрашивай)
Состав набора, годы, треки и рёбра ниже утверждены автором 18.10.2026 (ADR 0003), кроме
строк с пометкой «предложение» — их годы и тексты остаются черновиками с `ПРОВЕРИТЬ ДАТУ`.

## Шаги
1. **`Content/tracks.json`** — четыре трека (тексты `description` — черновики, помечай `ЧЕРНОВИК:`):

   | id | title | order |
   |---|---|---|
   | `hardware` | Аппаратная основа | 1 |
   | `computer-networks` | Компьютерные сети | 2 |
   | `frontend` | Front-end | 3 |
   | `process` | Процесс разработки | 6 |

2. **Семь `slice.json`** в `Content/slices/<id>/`, все `status: "draft"`, `examples: []`
   (кроме двух заглушек, см. шаг 3):

   | id | title | годы | треки | предшественники (slice @ track) | основание |
   |---|---|---|---|---|---|
   | `net-before-web-1992` | Сеть накануне веба, около 1992 | 1991–1992 | computer-networks | — | утверждено; тексты — `docs/slices/net-01_*.md` |
   | `static-page-1995` | Статическая страница, около 1995 | 1993–1995 | frontend | `net-before-web-1992` @ computer-networks | утверждено; тексты — `docs/slices/fe-01_*.md` |
   | `forms-cgi-1995` | Формы и CGI, около 1995 | 1994–1996 | frontend | `static-page-1995` @ frontend | утверждено (вариант А); тексты — `docs/slices/fe-02_*.md` |
   | `ajax-2005` | AJAX, 2005 | 2004–2005 | frontend | `forms-cgi-1995` @ frontend | предложение; ребро к треку данных — в сессии 08 |
   | `cpu-cycle` | Цикл процессора и регистры | 1945–1949 | hardware | — | предложение |
   | `punch-cards-1972` | Прогон программы с перфокартами, 1972 | 1971–1973 | hardware | `cpu-cycle` @ hardware | предложение |
   | `rup-vs-iterative-2001` | Одно решение двумя процессами: RUP против итераций | 1998–2002 | process | — | предложение |

   - Для трёх утверждённых срезов поля `introduces`, `relevance`, `linkBack`, `linkForward`,
     `whyNeededToReviewModelCode` бери **дословно** из разделов 2 и 4 их описаний в `docs/slices/`.
     Пометку `ЧЕРНОВИК:` в текстах сохраняй — её снимает автор.
   - Для четырёх срезов-предложений тексты пиши сам по brief (разделы 6, 7.3, 10.2), с пометкой
     `ЧЕРНОВИК:`; годы — с `ПРОВЕРИТЬ ДАТУ` в `linkBack`. `relevance`: ajax-2005 — `a`,
     cpu-cycle — `b`, punch-cards-1972 — `c`, rup-vs-iterative-2001 — `b`.
   - **Ребро `cpu-cycle → static-page-1995` удаляется** (решение автора).
   - После каждого файла — `python3 scripts/validate_manifest.py <файл>`.

3. **Заглушки примеров сессии 01 остаются** (`stub-static-page`, `stub-cpu-cycle`), чтобы пункт 6
   приёмочных критериев оставался проверяемым до сессии 03. В их `manifest.json` в `task`
   допиши: «Заглушка сессии 01, будет заменена примером "Справочник" (сессия 03)». Других
   примеров, процессов и `questions.json` в этой сессии **не создавай**.

4. **Тест на реальное содержание.** xUnit-тест, который загружает настоящую папка `Content/`
   через `ContentLoader` и проверяет: загрузка без ошибок; ровно четыре трека и семь срезов;
   граф без циклов; у `static-page-1995` предшественник `net-before-web-1992` по треку
   `computer-networks`, а `cpu-cycle` не предшественник. Плюс скрипт
   `scripts/validate_all.py`, который прогоняет `validate_manifest.py` по всем json в `Content/`
   и завершается с кодом 1 при первой ошибке. Запусти его перед PR и приложи вывод к отчёту.
   (Зачем: хук PostToolUse не срабатывает на файлы, записанные через Bash.)

5. **HTTP-тесты (Microsoft.AspNetCore.Mvc.Testing).** Автор разрешил пакет (ADR 0003).
   `dotnet add package` заблокирован хуком — добавь `PackageReference` правкой
   `tests/TimeSlices.Tests/TimeSlices.Tests.csproj` и `dotnet restore`. Тесты:
   анонимный `GET /` → редирект на вход (критерий 2); вошедший обучаемый, `GET /slices/forms-cgi-1995`
   до прохождения `static-page-1995` → 403 без содержания (критерий 3); `GET /content/<любой
   путь>` без токена → 404. Пользователей в тестах создавай в тестовой БД, не используй
   dev-пароли из `appsettings.Development.json`.

6. `dotnet build`, `dotnet test`, `python3 scripts/validate_all.py` — зелёные. Ручная проверка:
   `dotnet run`, вход обучаемым — на главной четыре трека; доступны `net-before-web-1992`,
   `cpu-cycle`, `rup-vs-iterative-2001`; «Готово» на `net-before-web-1992` открывает
   `static-page-1995`.

7. Перед PR — `code-reviewer`. **`era-reviewer` в этой сессии не запускай**: примеров и кода
   эпохи нет, а даты трёх утверждённых срезов проверены автором. Отчёт — по скиллу `session-report`.

## Не делать в этой сессии
Примеры, реализации, процессы, вопросы; изменение схемы манифеста; правку brief; трек данных
(он появится как заглушка-предшественник AJAX в сессии 08).

## Изменения в плане сессий (для справки, решение автора 18.10.2026)
- 03 — срезы `net-before-web-1992` и `static-page-1995` вместе: общий пример «Справочник»,
  реализации `ftp-1992`, `gopher-1992`, `web-1995` (описания в `docs/slices/`).
- 06 — `forms-cgi-1995`: пример «Справочник», реализация с формой поиска.
- Dynamic HTML с примером «Пасьянс» — первый срез после 0→1.

## Итог

### 1. Сделано
- В `Content/tracks.json` четыре трека: `hardware`, `computer-networks`, `frontend`, `process`. Описания помечены `ЧЕРНОВИК:`.
- Семь `slice.json` в статусе `draft`, граф без циклов.
  - Ребро `cpu-cycle → static-page-1995` удалено.
  - `static-page-1995` теперь требует `net-before-web-1992` по треку `computer-networks`.
  - Тексты трёх утверждённых срезов взяты дословно из разделов 2 и 4 их описаний.
  - Тексты четырёх срезов-предложений написаны по brief, все с пометкой `ЧЕРНОВИК:`.
- Заглушки `stub-static-page` и `stub-cpu-cycle` оставлены; в их `task` дописана фраза из плана.
- Добавлен `scripts/validate_all.py`. Он проверяет всё содержание и завершается с кодом 1 при первой ошибке.
- Тесты на настоящий `Content/` (`RealContentTests`): состав набора, отсутствие циклов, межтрековое ребро; «Готово» на `net-before-web-1992` открывает `static-page-1995`.
- HTTP-тесты (`HttpTests`, пакет `Microsoft.AspNetCore.Mvc.Testing` 10.0.12):
  - анонимный `GET /` → редирект на вход;
  - обучаемый открывает `/slices/forms-cgi-1995` → 403 без содержания;
  - `/content/...` без токена или с поддельным токеном → 404, с настоящим токеном → 200.
- `code-reviewer` нашёл пять существенных замечаний; все исправлены (коммит 896be10).

### 2. Сознательно не сделано и почему
- Примеры, реализации, процессы и `questions.json` не создавал: по плану они в сессиях 03 и 06.
- `era-reviewer` не запускал: так сказано в плане.
- Трек данных не создавал: он появится в сессии 08.
- Замечания ревьюера 6–9 оставил как есть (необязательные):
  - лишний тест на отсутствие циклов;
  - временные файлы тестов, если процесс упадёт;
  - пометки дат в `cpu-cycle`, см. раздел 4;
  - описание трека `hardware`, см. раздел 4.

### 3. Проверка
- `dotnet build TimeSlices.sln`: 0 предупреждений, 0 ошибок.
- `dotnet test TimeSlices.sln`: `Passed! - Failed: 0, Passed: 37, Skipped: 0, Total: 37`.
- `python3 scripts/validate_all.py`: код выхода 0, вывод:
  ```
  tracks.json: OK
  slices/ajax-2005/slice.json: OK
  slices/cpu-cycle/slice.json: OK
  slices/cpu-cycle/examples/stub-cpu-cycle/manifest.json: OK
  slices/forms-cgi-1995/slice.json: OK
  slices/net-before-web-1992/slice.json: OK
  slices/punch-cards-1972/slice.json: OK
  slices/rup-vs-iterative-2001/slice.json: OK
  slices/static-page-1995/slice.json: OK
  slices/static-page-1995/examples/stub-static-page/manifest.json: OK
  slices/cpu-cycle/examples/stub-cpu-cycle/implementations/stub/process/stub.json: OK (проверен через manifest.json)
  slices/static-page-1995/examples/stub-static-page/implementations/stub/process/stub.json: OK (проверен через manifest.json)
  Итого: проверено 12, пропущено 0, ошибок нет
  ```
- Отрицательные проверки `validate_all.py` на копии `Content/` в scratchpad. Во всех трёх случаях код выхода 1:
  - `relevance: "x"`;
  - лишний файл `Slice.JSON.json`;
  - процесс, не указанный в манифесте.
- Ручная проверка: `dotnet run`, затем curl с входом под `learner@timeslices.local`.
  - `/` без входа → 302 на `/Identity/Account/Login`.
  - На главной четыре трека. Доступны `cpu-cycle`, `net-before-web-1992`, `rup-vs-iterative-2001`, остальные недоступны.
  - «Готово» на `net-before-web-1992` → «Пройден». `static-page-1995` стал «Доступен» и открывается (200), `forms-cgi-1995` остался недоступен.
  - В браузере глазами не смотрел.
  - Состояние dev-обучаемого после этой проверки осталось в локальном `app.db` контейнера.
- HTTP-тесты не трогают `app.db`: время его изменения после прогона не поменялось.

### 4. Допущения, принятые за автора
- К `introduces` трёх утверждённых срезов добавлен префикс `ЧЕРНОВИК: `.
  - В описаниях его нет, но для текстов, которые видит обучаемый, его требует CLAUDE.md.
  - У `static-page-1995` в описании прямо сказано, что `introduces` нужно утвердить заново.
- `forms-cgi-1995`:
  - `relevance: "a"` взято из описания, где это помечено как предложение; план это поле не задаёт;
  - префикс `ЧЕРНОВИК ИДЕИ, формулирует автор:` в `whyNeededToReviewModelCode` сохранён дословно.
- У предшественников поле `why` взято из таблиц раздела 1 описаний, с префиксом `ЧЕРНОВИК:`.
- В заглушке `stub-cpu-cycle` в `task` стоит та же фраза, что в плане: «будет заменена примером "Справочник" (сессия 03)». Но у `cpu-cycle` «Справочника» в плане нет. Фразу, видимо, нужно уточнить.
- В `Program.cs` добавлено `public partial class Program;`. Это одна строка кода оболочки, она нужна только для тестов.
- `ПРОВЕРИТЬ ДАТУ`:
  - **`ajax-2005`: 2004–2005** (предложение);
  - **`cpu-cycle`: 1945–1949** (предложение). В `introduces` сказано «программа хранится в памяти вместе с данными», то есть речь о хранимой программе как о решении этих лет;
  - **`punch-cards-1972`: 1971–1973** (предложение). В `linkForward` у `cpu-cycle` есть фраза «в 1972 году… многочасовое ожидание распечатки»;
  - **`rup-vs-iterative-2001`: 1998–2002** (предложение).
- Описание трека `hardware` («Предшественник для всех треков») взято из brief. После удаления ребра это расходится с графом: у `computer-networks` и `process` нет предшественника в треке `hardware`.

### 5. Черновик ADR
`docs/decisions/0004-content-checks-and-http-tests.md` (proposed). Что в нём:
- тест фиксирует состав первого набора;
- `validate_all.py` и что он считает ошибкой;
- HTTP-тесты через `WebApplicationFactory`: среда `Testing`, временная SQLite-БД, вход через настоящую форму;
- эталонные фрагменты текстов в тестах берутся из каталога.

### 6. Следующая сессия
1. Решить по датам срезов-предложений и по тексту в `task` заглушки `stub-cpu-cycle`.
2. Утвердить или отклонить ADR 0004.
3. Сессия 03: пример «Справочник» для `net-before-web-1992` и `static-page-1995`, реализации `ftp-1992`, `gopher-1992`, `web-1995`. Заглушку `stub-static-page` заменить.
