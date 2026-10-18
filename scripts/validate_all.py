#!/usr/bin/env python3
"""Прогоняет validate_manifest.py по всем файлам содержания в Content/.

Зачем: хук PostToolUse срабатывает только на Edit|Write, а не на файлы, записанные через Bash.
- tracks.json, slice.json, manifest.json, questions.json — через validate_manifest.py;
- implementations/<impl-id>/process/*.json — их проверяет validate_manifest.py при проверке
  манифеста; процесс, который манифест не перечисляет, — ошибка (его бы никто не проверил);
- прочие json внутри implementations/<impl-id>/ — часть статической сборки, пропускаются;
- любой другой json (например, опечатка в имени Slice.json) — ошибка.
Код выхода 1 — при первой ошибке.
Вызов: python3 scripts/validate_all.py [путь к Content/]
"""
import json
import os
import subprocess
import sys

KNOWN = ("tracks.json", "slice.json", "manifest.json", "questions.json")

here = os.path.dirname(os.path.abspath(__file__))
root = sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, "..", "src", "TimeSlices.Web", "Content")
root = os.path.abspath(root)
if not os.path.isfile(os.path.join(root, "tracks.json")):
    print(f"Не найден {os.path.join(root, 'tracks.json')}")
    sys.exit(1)


def fail(msg):
    print(msg)
    sys.exit(1)


def implementation_part(rel):
    """Часть пути после implementations/<impl-id>/ или None, если файл не внутри реализации."""
    parts = rel.split(os.sep)
    if "implementations" in parts:
        i = parts.index("implementations")
        if len(parts) > i + 2:
            return parts[i + 2:]
    return None


def processes_listed_in(manifest):
    """Абсолютные пути процессов, перечисленных в манифесте."""
    base = os.path.dirname(manifest)
    with open(manifest, encoding="utf-8") as f:
        d = json.load(f)
    return {os.path.normpath(os.path.join(base, "implementations", im.get("id", ""), pr.get("file", "")))
            for im in d.get("implementations", []) for pr in im.get("processes", []) or []
            if isinstance(pr, dict)}


checked = skipped = 0
listed, process_files = set(), []
for dirpath, dirnames, filenames in os.walk(root):
    dirnames.sort()
    for name in sorted(filenames):
        if not name.endswith(".json"):
            continue
        path = os.path.join(dirpath, name)
        rel = os.path.relpath(path, root)
        inside = implementation_part(rel)
        if inside is not None:
            if len(inside) == 2 and inside[0] == "process":
                process_files.append(os.path.normpath(path))
            else:
                skipped += 1
                print(f"{rel}: пропущен (файл статической сборки)")
            continue
        if name not in KNOWN:
            fail(f"{rel}: неизвестный файл содержания (ожидаются {', '.join(KNOWN)})")
        r = subprocess.run([sys.executable, os.path.join(here, "validate_manifest.py"), path],
                           capture_output=True, text=True)
        print(r.stdout.rstrip().replace(path, rel))
        if r.returncode != 0:
            fail(r.stderr.rstrip())
        if name == "manifest.json":
            listed |= processes_listed_in(path)
        checked += 1

for pf in process_files:
    rel = os.path.relpath(pf, root)
    if pf not in listed:
        fail(f"{rel}: процесс не указан ни в одном manifest.json — его никто не проверяет")
    print(f"{rel}: OK (проверен через manifest.json)")
    checked += 1

print(f"Итого: проверено {checked}, пропущено {skipped}, ошибок нет")
