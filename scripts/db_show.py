"""Показать базу данных приложения (SQLite, app.db) — только чтение.

Использование (из корня репозитория):
  python scripts/db_show.py                 список таблиц с числом строк
  python scripts/db_show.py <таблица> [N]   первые N строк таблицы (по умолчанию 20)
  python scripts/db_show.py --sql "<запрос>"  произвольный SELECT

База открывается в режиме только для чтения: случайно изменить её этим скриптом нельзя.
Файл ищется в корне репозитория и в src/TimeSlices.Web (зависит от того, откуда запускали приложение).
"""
import sqlite3
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CANDIDATES = [ROOT / "app.db", ROOT / "src" / "TimeSlices.Web" / "app.db"]
MAX_CELL = 60  # длинные значения (хеши паролей, тексты ответов) обрезаются


def find_db() -> Path:
    for p in CANDIDATES:
        if p.exists():
            return p
    print("Файл app.db не найден. Искал:")
    for p in CANDIDATES:
        print("  ", p)
    print("База создаётся при первом запуске приложения (bat\\run-app.bat).")
    sys.exit(1)


def cell(v) -> str:
    s = "NULL" if v is None else str(v).replace("\r", " ").replace("\n", " ")
    return s if len(s) <= MAX_CELL else s[: MAX_CELL - 1] + "…"


def print_rows(cur: sqlite3.Cursor) -> None:
    names = [d[0] for d in cur.description]
    rows = [[cell(v) for v in r] for r in cur.fetchall()]
    widths = [max([len(n)] + [len(r[i]) for r in rows]) for i, n in enumerate(names)]
    line = lambda vals: " | ".join(v.ljust(w) for v, w in zip(vals, widths))
    print(line(names))
    print("-+-".join("-" * w for w in widths))
    for r in rows:
        print(line(r))
    print(f"({len(rows)} строк)")


def main() -> None:
    db = find_db()
    con = sqlite3.connect(f"file:{db.as_posix()}?mode=ro", uri=True)
    print(f"База: {db}\n")
    args = sys.argv[1:]
    tables = [r[0] for r in con.execute(
        "select name from sqlite_master where type='table' and name not like 'sqlite_%' order by name")]

    if not args:
        width = max(len(t) for t in tables) if tables else 0
        for t in tables:
            n = con.execute(f'select count(*) from "{t}"').fetchone()[0]
            print(f"{t.ljust(width)}  {n:>6} строк")
        print("\nСтроки таблицы:  bat\\db-show.bat <таблица> [N]")
        print('Запрос:          bat\\db-show.bat --sql "select ..."')
        return

    if args[0] == "--sql":
        if len(args) < 2:
            print('Нужен запрос: --sql "select ..."')
            sys.exit(1)
        print_rows(con.execute(args[1]))
        return

    table = args[0]
    if table not in tables:
        match = [t for t in tables if t.lower() == table.lower()]
        if not match:
            print(f"Нет таблицы «{table}». Есть: {', '.join(tables)}")
            sys.exit(1)
        table = match[0]
    limit = int(args[1]) if len(args) > 1 else 20
    print_rows(con.execute(f'select * from "{table}" limit ?', (limit,)))


if __name__ == "__main__":
    main()
