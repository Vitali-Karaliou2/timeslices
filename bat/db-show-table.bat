@echo off
rem Show rows of ONE table from the app database (SQLite app.db), read-only.
rem Edit the table name (and row limit) in the two lines below, then double-click this file.
rem Tables: AspNetUsers, AspNetRoles, AspNetUserRoles, UserSliceStates, Attempts, Answers, __EFMigrationsHistory

set TABLE=UserSliceStates
set LIMIT=20

chcp 65001 >nul
set PYTHONIOENCODING=utf-8
cd /d "%~dp0.."
python scripts\db_show.py "%TABLE%" %LIMIT%
pause
