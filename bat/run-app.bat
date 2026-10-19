@echo off
rem Run TimeSlices at http://localhost:5000 (Development). Stop: Ctrl+C.
chcp 65001 >nul
cd /d "%~dp0.."
echo Запуск TimeSlices: http://localhost:5000  (остановить - Ctrl+C)
rem Open the browser a few seconds later, when the server is up.
start "" /min cmd /c "timeout /t 6 /nobreak >nul & start "" http://localhost:5000"
dotnet run --project src\TimeSlices.Web --launch-profile http
pause
