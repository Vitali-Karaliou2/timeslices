#!/usr/bin/env python3
"""Валидатор файлов содержания коллекции: tracks.json, slice.json, manifest.json, questions.json, article.json.

Схема — в docs/spec/manifest-schema.md. Скрипт намеренно простой (без jsonschema),
чтобы работать в любой среде без зависимостей. Код выхода 1 = есть нарушения.
Вызов: python3 scripts/validate_manifest.py <file.json>
"""
import json
import os
import re
import sys

ID_RE = re.compile(r"^[a-z0-9][a-z0-9-]*$")
RELEVANCE = {"a", "b", "c"}          # (а) используется моделями, (б) вытеснено, но нужно, (в) исторично
PROCESS_TYPES = {"execution", "creation"}
STATUSES = {"draft", "reviewed", "approved"}
HEADING_KEYS = {"linkBack", "introduces", "linkForward", "relevance"}   # ADR 0013, п. 3
HEADING_MAX = 80
# Ссылка на справку: «статья» или «статья#раздел» (ADR 0013, п. 7). Что статья и раздел существуют,
# проверяет validate_all.py — там видны все файлы.
REF_RE = re.compile(r"^[a-z0-9][a-z0-9-]*(#[a-z0-9][a-z0-9-]*)?$")

errors = []


def err(msg):
    errors.append(msg)


def require(obj, keys, ctx):
    for k in keys:
        if k not in obj:
            err(f"{ctx}: нет обязательного поля '{k}'")


def check_id(value, ctx):
    if not isinstance(value, str) or not ID_RE.match(value):
        err(f"{ctx}: id '{value}' должен быть kebab-case (a-z, 0-9, -)")


def check_year(value, ctx):
    if not isinstance(value, int) or not (1940 <= value <= 2030):
        err(f"{ctx}: год '{value}' вне диапазона 1940..2030")


def validate_tracks(d):
    if not isinstance(d, list):
        err("tracks.json: ожидается список треков")
        return
    seen = set()
    for i, t in enumerate(d):
        ctx = f"tracks[{i}]"
        require(t, ["id", "title", "order"], ctx)
        if "id" in t:
            check_id(t["id"], ctx)
            if t["id"] in seen:
                err(f"{ctx}: дублирующийся id {t['id']}")
            seen.add(t["id"])


def validate_slice(d, path):
    ctx = "slice.json"
    require(d, ["id", "title", "yearFrom", "yearTo", "tracks", "introduces",
                "relevance", "prerequisites", "linkBack", "linkForward",
                "whyNeededToReviewModelCode", "status"], ctx)
    if "id" in d:
        check_id(d["id"], ctx)
        folder = os.path.basename(os.path.dirname(os.path.abspath(path)))
        if folder != d["id"]:
            err(f"{ctx}: id '{d['id']}' не совпадает с именем папки '{folder}'")
    for k in ("yearFrom", "yearTo"):
        if k in d:
            check_year(d[k], ctx)
    if "yearFrom" in d and "yearTo" in d and d["yearFrom"] > d["yearTo"]:
        err(f"{ctx}: yearFrom > yearTo")
    if d.get("relevance") not in RELEVANCE:
        err(f"{ctx}: relevance должен быть одним из {sorted(RELEVANCE)}")
    if "relevanceNote" in d:   # необязательное, ADR 0010
        note = d["relevanceNote"]
        if not isinstance(note, str) or not note.strip():
            err(f"{ctx}: relevanceNote, если есть, — непустая строка")
    if not isinstance(d.get("tracks"), list) or not d.get("tracks"):
        err(f"{ctx}: tracks — непустой список id треков")
    for p in d.get("prerequisites", []) or []:
        if not isinstance(p, dict) or "slice" not in p or "track" not in p:
            err(f"{ctx}: каждый элемент prerequisites — объект {{slice, track, why}}")
        elif p.get("slice") == d.get("id"):
            err(f"{ctx}: срез не может быть предшественником самого себя")
    why = d.get("whyNeededToReviewModelCode", "")
    if not isinstance(why, str) or len(why.strip()) < 40:
        err(f"{ctx}: whyNeededToReviewModelCode пуст или слишком короток — "
            "по brief (риск 3) срез без этого ответа в коллекцию не включается")
    if d.get("status") not in STATUSES:
        err(f"{ctx}: status ∈ {{draft, reviewed, approved}}")
    if "headings" in d:
        validate_headings(d["headings"], ctx)
    if "refs" in d:
        check_refs(d["refs"], f"{ctx}: refs")


def validate_headings(h, ctx):
    """Необязательные подписи разделов страницы среза (ADR 0013, п. 3–4)."""
    if not isinstance(h, dict):
        err(f"{ctx}: headings, если есть, — объект")
        return
    for k, v in h.items():
        if k not in HEADING_KEYS:
            err(f"{ctx}: headings: неизвестный ключ '{k}' (допустимы {', '.join(sorted(HEADING_KEYS))})")
        if not isinstance(v, str) or not v.strip():
            err(f"{ctx}: headings.{k} — непустая строка")
        elif len(v) > HEADING_MAX:
            err(f"{ctx}: headings.{k} длиннее {HEADING_MAX} символов")


def check_refs(refs, ctx):
    if not isinstance(refs, list):
        err(f"{ctx} — список ссылок вида 'статья' или 'статья#раздел'")
        return
    for r in refs:
        if not isinstance(r, str) or not REF_RE.match(r):
            err(f"{ctx}: ссылка '{r}' — ожидается 'статья' или 'статья#раздел'")


def validate_article(d, path):
    """Справочная статья Content/reference/<id>/article.json (ADR 0013, п. 6)."""
    ctx = "article.json"
    require(d, ["id", "title", "summary", "sections", "see", "sources", "status"], ctx)
    if "id" in d:
        check_id(d["id"], ctx)
        folder = os.path.basename(os.path.dirname(os.path.abspath(path)))
        if folder != d["id"]:
            err(f"{ctx}: id '{d['id']}' не совпадает с именем папки '{folder}'")
    for k in ("title", "summary"):
        if k in d and (not isinstance(d[k], str) or not d[k].strip()):
            err(f"{ctx}: {k} — непустая строка")
    sections = d.get("sections")
    if "sections" in d and (not isinstance(sections, list) or not sections):
        err(f"{ctx}: sections — непустой список разделов")
    seen = set()
    for i, sec in enumerate(sections if isinstance(sections, list) else []):
        c = f"{ctx}.sections[{i}]"
        if not isinstance(sec, dict):
            err(f"{c}: раздел — объект {{id, heading, asOf, paragraphs}}")
            continue
        require(sec, ["id", "heading", "asOf", "paragraphs"], c)
        if "id" in sec:
            check_id(sec["id"], c)
            if sec["id"] in seen:
                err(f"{c}: id раздела '{sec['id']}' повторяется")
            seen.add(sec["id"])
        if "heading" in sec and (not isinstance(sec["heading"], str) or not sec["heading"].strip()):
            err(f"{c}: heading — непустая строка")
        if "asOf" in sec and sec["asOf"] is not None:
            # bool в Python — подкласс int; true не год.
            if isinstance(sec["asOf"], bool):
                err(f"{c}: asOf — год или null")
            else:
                check_year(sec["asOf"], f"{c}: asOf")
        paragraphs = sec.get("paragraphs")
        if "paragraphs" in sec and (not isinstance(paragraphs, list) or not paragraphs
                                    or not all(isinstance(p, str) and p.strip() for p in paragraphs)):
            err(f"{c}: paragraphs — непустой список непустых строк")
    see = d.get("see")
    if "see" in d:
        if not isinstance(see, list):
            err(f"{ctx}: see — список id статей")
        else:
            reference_dir = os.path.dirname(os.path.dirname(os.path.abspath(path)))
            for s_id in see:
                if not isinstance(s_id, str) or not ID_RE.match(s_id):
                    err(f"{ctx}: see: id '{s_id}' должен быть kebab-case")
                elif not os.path.isfile(os.path.join(reference_dir, s_id, "article.json")):
                    err(f"{ctx}: see: статья '{s_id}' не найдена (нет reference/{s_id}/article.json)")
    sources = d.get("sources")
    if "sources" in d:
        if not isinstance(sources, list):
            err(f"{ctx}: sources — список {{title, url}}")
        else:
            for i, src in enumerate(sources):
                c = f"{ctx}.sources[{i}]"
                if not isinstance(src, dict):
                    err(f"{c}: источник — объект {{title, url}}")
                    continue
                require(src, ["title", "url"], c)
                if "url" in src and (not isinstance(src["url"], str) or not src["url"].startswith("https://")):
                    err(f"{c}: url должен начинаться с https://")
    if "status" in d and d["status"] not in STATUSES:
        err(f"{ctx}: status ∈ {{draft, reviewed, approved}}")


def validate_manifest(d, path):
    ctx = "manifest.json"
    require(d, ["id", "title", "task", "introducedInSlice", "complexity", "implementations"], ctx)
    if "id" in d:
        check_id(d["id"], ctx)
    impls = d.get("implementations", [])
    if not isinstance(impls, list) or not impls:
        err(f"{ctx}: implementations — непустой список")
        return
    base = os.path.dirname(os.path.abspath(path))
    for i, im in enumerate(impls):
        c = f"{ctx}.implementations[{i}]"
        require(im, ["id", "slice", "stack", "entry", "codeMode", "codeFiles", "processes", "anachronismCheck"], c)
        if "id" in im:
            check_id(im["id"], c)
        if im.get("codeMode") not in {"executable", "emulated"}:
            err(f"{c}: codeMode ∈ {{executable, emulated}}")
        entry = im.get("entry")
        if isinstance(entry, str):
            full = os.path.join(base, "implementations", im.get("id", ""), entry)
            if not os.path.exists(full):
                err(f"{c}: entry '{entry}' не найден по пути {full}")
        for pr in im.get("processes", []) or []:
            pc = f"{c}.processes"
            if not isinstance(pr, dict):
                err(f"{pc}: элемент должен быть объектом")
                continue
            require(pr, ["id", "type", "file"], pc)
            if pr.get("type") not in PROCESS_TYPES:
                err(f"{pc}: type ∈ {sorted(PROCESS_TYPES)}")
            pf = os.path.join(base, "implementations", im.get("id", ""), pr.get("file", ""))
            if pr.get("file") and not os.path.exists(pf):
                err(f"{pc}: файл процесса не найден: {pf}")
            else:
                validate_process_file(pf, pc)
        ac = im.get("anachronismCheck")
        if not isinstance(ac, dict) or ac.get("by") not in {"human", "pending"}:
            err(f"{c}: anachronismCheck = {{by: human|pending, date, notes}}; проверка на анахронизмы — "
                "только человеком (brief, риск 1)")


def validate_process_file(pf, ctx):
    try:
        with open(pf, encoding="utf-8") as f:
            p = json.load(f)
    except Exception as e:
        err(f"{ctx}: не читается {pf}: {e}")
        return
    frames = p.get("frames", [])
    if not frames:
        err(f"{ctx}: процесс без кадров")
        return
    if not any(fr.get("stop") for fr in frames):
        err(f"{ctx}: в процессе нет ни одного стоп-кадра (brief 10.3: минимум один)")
    for i, fr in enumerate(frames):
        if "text" not in fr:
            err(f"{ctx}.frames[{i}]: нет text")
        if fr.get("stop") and not fr.get("detail"):
            err(f"{ctx}.frames[{i}]: стоп-кадр без detail (что видит пользователь внутри шага)")
        if isinstance(fr.get("detail"), dict) and "refs" in fr["detail"]:
            check_refs(fr["detail"]["refs"], f"{ctx}.frames[{i}].detail.refs")


def validate_questions(d):
    ctx = "questions.json"
    qs = d.get("questions") if isinstance(d, dict) else d
    if not isinstance(qs, list) or len(qs) < 5:
        err(f"{ctx}: не менее пяти вопросов (brief 10.3)")
        return
    for i, q in enumerate(qs):
        c = f"{ctx}[{i}]"
        require(q, ["id", "prompt", "referenceReasoning", "followUpTemplate"], c)
        if "options" in q or "choices" in q:
            err(f"{c}: вопросы с выбором ответа запрещены (brief 8.2)")
        if len(str(q.get("referenceReasoning", ""))) < 40:
            err(f"{c}: referenceReasoning слишком короток")


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    path = sys.argv[1]
    try:
        with open(path, encoding="utf-8") as f:
            d = json.load(f)
    except Exception as e:
        print(f"Не удалось прочитать {path}: {e}")
        sys.exit(1)
    name = os.path.basename(path)
    if name == "tracks.json":
        validate_tracks(d)
    elif name == "slice.json":
        validate_slice(d, path)
    elif name == "manifest.json":
        validate_manifest(d, path)
    elif name == "questions.json":
        validate_questions(d)
    elif name == "article.json":
        validate_article(d, path)
    else:
        print(f"Неизвестный тип файла: {name}")
        sys.exit(2)
    if errors:
        print(f"{path}: {len(errors)} нарушений")
        for e in errors:
            print(" -", e)
        sys.exit(1)
    print(f"{path}: OK")


if __name__ == "__main__":
    main()
