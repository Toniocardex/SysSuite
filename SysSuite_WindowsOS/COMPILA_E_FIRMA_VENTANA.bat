@echo off
REM Se la finestra si chiude subito, usa QUESTO file: tiene aperto il prompt dopo l'errore.
cd /d "%~dp0"
call "%~dp0COMPILA_E_FIRMA.bat"
echo.
echo Codice uscita: %ERRORLEVEL%
echo.
pause
