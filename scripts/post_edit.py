#!/usr/bin/env python3
"""PostToolUse-хук для Edit|Write.

1. Если изменён manifest.json / slice.json / questions.json — прогоняет валидатор
   и возвращает Claude список нарушений (код 2 → Claude видит stderr и исправляет).
2. Если изменён .cs-файл — ничего не форматирует (dotnet format медленный);
   оставлено место для включения позже.
"""
import json
import os
import subprocess
import sys

try:
    data = json.load(sys.stdin)
except Exception:
    sys.exit(0)

path = (data.get("tool_input") or {}).get("file_path", "")
if not path:
    sys.exit(0)

name = os.path.basename(path)
root = os.environ.get("CLAUDE_PROJECT_DIR", ".")

if name in ("manifest.json", "slice.json", "questions.json", "tracks.json"):
    r = subprocess.run(
        [sys.executable, os.path.join(root, "scripts", "validate_manifest.py"), path],
        capture_output=True, text=True,
    )
    if r.returncode != 0:
        print(r.stdout + r.stderr, file=sys.stderr)
        sys.exit(2)

sys.exit(0)
