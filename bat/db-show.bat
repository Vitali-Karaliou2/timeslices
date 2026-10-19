@echo off
rem Show the app database (SQLite app.db), read-only.
rem   db-show.bat                    - tables with row counts
rem   db-show.bat UserSliceStates 50 - first 50 rows of a table
rem   db-show.bat --sql "select ..." - any SELECT
chcp 65001 >nul
set PYTHONIOENCODING=utf-8
cd /d "%~dp0.."
python scripts\db_show.py %*
if "%~1"=="" pause
