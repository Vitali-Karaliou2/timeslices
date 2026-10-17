# Схема файлов содержания коллекции (черновик, редакция 17.10.2026)

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
  "prerequisites": [
    { "slice": "cpu-cycle", "track": "hardware", "why": "что такое клиент, сервер и файл" }
  ],
  "linkBack": "ЧЕРНОВИК: связь с предыдущими срезами ...",
  "linkForward": "ЧЕРНОВИК: формы и первая динамика (1999), AJAX (2005)",
  "whyNeededToReviewModelCode": "ЧЕРНОВИК: модель до сих пор порождает семантически неверную разметку ...",
  "examples": ["solitaire"],
  "status": "draft"
}
```
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
      "detail": { "codeFile": "code/index.html", "lines": "1-40", "explanation": "ЧЕРНОВИК: ..." } }
  ]
}
```
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
