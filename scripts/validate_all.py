#!/usr/bin/env python3
"""Прогоняет validate_manifest.py по всем файлам содержания в Content/.

Зачем: хук PostToolUse срабатывает только на Edit|Write, а не на файлы, записанные через Bash.
Проверяются tracks.json, slice.json, manifest.json, questions.json; файлы процессов
(process/*.json) проверяет валидатор манифеста, остальные json внутри статических сборок
реализаций — часть сборки, и они пропускаются. Код выхода 1 — при первой ошибке.
Вызов: python3 scripts/validate_all.py [путь к Content/]
"""
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

checked = skipped = 0
for dirpath, dirnames, filenames in os.walk(root):
    dirnames.sort()
    for name in sorted(filenames):
        if not name.endswith(".json"):
            continue
        path = os.path.join(dirpath, name)
        rel = os.path.relpath(path, root)
        if name not in KNOWN:
            skipped += 1
            print(f"{rel}: пропущен (процесс или файл сборки; процессы проверяются через manifest.json)")
            continue
        r = subprocess.run([sys.executable, os.path.join(here, "validate_manifest.py"), path],
                           capture_output=True, text=True)
        print(r.stdout.rstrip().replace(path, rel))
        if r.returncode != 0:
            print(r.stderr.rstrip())
            sys.exit(1)
        checked += 1

print(f"Итого: проверено {checked}, пропущено {skipped}, ошибок нет")
