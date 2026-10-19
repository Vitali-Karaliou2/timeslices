@echo off
rem Build TimeSlices and run all tests (xUnit). Needs Python 3 in PATH for validator tests.
chcp 65001 >nul
cd /d "%~dp0.."
echo Прогон всех тестов TimeSlices...
dotnet test TimeSlices.sln
if errorlevel 1 (echo. & echo ЕСТЬ УПАВШИЕ ТЕСТЫ - см. вывод выше.) else (echo. & echo Все тесты прошли.)
pause
