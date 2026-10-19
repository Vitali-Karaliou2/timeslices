# Схема файлов содержания коллекции (черновик, редакция 19.10.2026)

Оболочка читает только эти файлы. Любое изменение схемы — через ADR в `docs/decisions/`
и с согласия автора. Проверка: `python3 scripts/validate_manifest.py <file>`.

## Content/tracks.json
```json
[
  { "id": "frontend",  "title": "Front-end",               "order": 3, "description": "..." },
  { "id": "hardware",  "title": "Аппаратная основа",       "order": 1, "description": "..." },
  { "id": "process",   "title": "Процесс разработки",      "order": 6, "description": "..." }
]
```

## Content/slices/<slice-id>/slice.json
```json
{
  "id": "static-page-1995",
  "title": "Статическая страница, около 1995",
  "yearFrom": 1994, "yearTo": 1996,
  "tracks": ["frontend"],
  "introduces": "Документ как интерфейс: HTML без скриптов и без CSS",
  "relevance": "b",
  "relevanceNote": "ЧЕРНОВИК: почему у среза такая пометка актуальности (показывается обучаемому рядом с ней)",
  "prerequisites": [
    { "slice": "cpu-cycle", "track": "hardware", "why": "что такое клиент, сервер и файл" }
  ],
  "linkBack": "ЧЕРНОВИК: связь с предыдущими срезами ...",
  "linkForward": "ЧЕРНОВИК: формы и первая динамика (1999), AJAX (2005)",
  "whyNeededToReviewModelCode": "ЧЕРНОВИК: модель до сих пор порождает семантически неверную разметку ...",
  "examples": ["solitaire"],
  "headings": { "introduces": "ЧЕРНОВИК: Что представлял собой Интернет к началу 1990-х" },
  "refs": ["internet-before-web", "dns#1992"],
  "status": "draft"
}
```
`headings` (необязательное, ADR 0013, п. 3) — замена подписей разделов страницы среза. Ключи только
`linkBack`, `introduces`, `linkForward`, `relevance`; значение — непустая строка до 80 символов; пропущенный
ключ — подпись по умолчанию («Что было до», «Что меняется», «Что будет после», «Как это связано
с сегодняшним днём?»). Порядок и содержание разделов не меняются.
`refs` (необязательное, ADR 0013, п. 7) — ссылки на справку: `"статья"` или `"статья#раздел"`;
на странице среза — строка «Справка: …» после раздела «Что меняется». Ссылка на несуществующую статью
или раздел — ошибка загрузки каталога и `validate_all.py`.
`relevanceNote` (необязательное, ADR 0010) — пояснение к пометке актуальности для обучаемого; берётся из раздела 4 описания среза.
`relevance`: `a` — используется и в коде современных моделей; `b` — вытеснено, но без него непонятны
последующие; `c` — исторично, для контекста. `status`: `draft` → `reviewed` (era-reviewer) → `approved` (автор).

## Content/slices/<slice-id>/examples/<example-id>/manifest.json
```json
{
  "id": "solitaire",
  "title": "Пасьянс",
  "task": "Одна и та же игра: desktop-приложение и веб-страница",
  "introducedInSlice": "static-page-1995",
  "complexity": 1,
  "implementations": [
    {
      "id": "web-1995",
      "slice": "static-page-1995",
      "stack": "HTML 2.0, без CSS и JS",
      "entry": "index.html",
      "codeMode": "executable",
      "codeFiles": [ { "path": "code/index.html", "note": "ключевые строки: 12, 30" } ],
      "processes": [ { "id": "request-response", "type": "execution", "file": "process/request-response.json" } ],
      "anachronismCheck": { "by": "pending", "date": null, "notes": "ПРОВЕРИТЬ ДАТУ: тег <table> в HTML 2.0" }
    }
  ]
}
```
Папка реализации: `implementations/<impl-id>/` — всё, что нужно для показа в iframe (`entry`),
`code/` — код для показа, `process/*.json` — процессы.

## process/<process-id>.json
```json
{
  "id": "request-response",
  "type": "execution",
  "variant": null,
  "startAt": "1995-03-01T09:00",
  "frames": [
    { "order": 1, "at": "1995-03-01T09:00", "durationNote": "", "text": "Браузер запрашивает файл", "stop": false },
    { "order": 2, "at": "1995-03-01T09:00", "durationNote": "",
      "text": "Сервер отдаёт файл как есть", "stop": true,
      "detail": { "codeFile": "code/index.html", "lines": "1-40", "explanation": "ЧЕРНОВИК: ...",
                  "refs": ["dns#1992"] } }
  ]
}
```
`detail.refs` (необязательное, ADR 0013, п. 7) — ссылки на справку из стоп-кадра в том же формате, что `refs`
среза; строка «Справка: …» под пояснением в проигрывателе и в списке кадров без JS.
`type`: `execution` (как программа выполняется) | `creation` (как пример создавался); `variant` — для
альтернативных процессов создания (`rup`, `iterative`).

## Content/slices/<slice-id>/questions.json
```json
{
  "questions": [
    {
      "id": "q1",
      "prompt": "Почему в строке 12 ...?",
      "referenceReasoning": "Эталонное обоснование для оценивающей модели (не показывается обучаемому)",
      "followUpTemplate": "Вы написали: «{answer}». Что изменится, если ...?"
    }
  ]
}
```
Вопросов с выбором ответа нет (brief 8.2). Минимум пять на срез (brief 10.3).

## Content/reference/<article-id>/article.json
Справочная статья (ADR 0013, п. 5–6): текст, общий для нескольких срезов. Папка, а не файл, — чтобы рядом
можно было положить картинку или схему.
```json
{
  "id": "dns",
  "title": "Система имён DNS",
  "summary": "ЧЕРНОВИК: как имя машины превращается в числовой адрес",
  "sections": [
    { "id": "1992", "heading": "Как было к 1992 году", "asOf": 1992, "paragraphs": ["ЧЕРНОВИК: ..."] },
    { "id": "now", "heading": "Как сейчас", "asOf": null, "paragraphs": ["ЧЕРНОВИК: ..."] }
  ],
  "see": ["top-level-domains"],
  "sources": [ { "title": "RFC 1034", "url": "https://www.rfc-editor.org/rfc/rfc1034" } ],
  "status": "draft"
}
```
`id` совпадает с именем папки; `sections[].id` уникальны и служат якорями (`/reference/dns#1992`);
`asOf` — год 1940..2030, к которому относится раздел, или `null` («сейчас»); `paragraphs` — непустой массив
непустых строк без Markdown и без ссылок внутри; `see` — id существующих статей; `sources[].url` начинается
с `https://`, источники показываются обучаемому; `status`: `draft` → `reviewed` → `approved`.
Страницы `/reference` и `/reference/{id}` открыты без входа; «Где упоминается» строит каталог по `refs` и `detail.refs`.
