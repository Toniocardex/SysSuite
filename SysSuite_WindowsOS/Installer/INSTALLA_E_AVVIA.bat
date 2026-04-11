@echo off
chcp 65001 >nul
title SysSuite One - Installazione

echo.
echo  =====================================================
echo   SYSSUITE ONE v1.0.0 - Installazione prerequisiti
echo   Autore: Antonio Cardelli - Milano, IT
echo  =====================================================
echo.

:: ── Verifica Windows 10 1809+ ────────────────────────────────
for /f "tokens=4-5 delims=. " %%i in ('ver') do set WIN_BUILD=%%i
if %WIN_BUILD% LSS 17763 (
    echo  [ERRORE] SysSuite One richiede Windows 10 versione 1809 o superiore.
    echo  Versione rilevata: %WIN_BUILD%
    pause
    exit /b 1
)
echo  [OK] Windows build: %WIN_BUILD%

:: ── Verifica privilegi admin ─────────────────────────────────
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo  [INFO] Richiesta elevazione privilegi...
    PowerShell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)
echo  [OK] Privilegi Amministratore

:: ── Installa Windows App SDK se mancante ─────────────────────
echo.
echo  [1/2] Verifica Windows App SDK...

PowerShell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$pkg = Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime*' -EA SilentlyContinue; ^
    if ($pkg) { Write-Host '  [OK] Windows App SDK trovato: ' + $pkg.Version; exit 0 } ^
    else { Write-Host '  [INFO] Windows App SDK non trovato'; exit 1 }"

if %errorLevel% neq 0 (
    echo.
    echo  Installazione Windows App SDK in corso...
    echo  (potrebbe richiedere 1-2 minuti)
    echo.
    PowerShell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install_WindowsAppSDK.ps1" -Silent
    if %errorLevel% neq 0 (
        echo.
        echo  [AVVISO] Installazione automatica fallita.
        echo  Scarica manualmente da:
        echo  https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads
        echo.
        pause
        exit /b 1
    )
) else (
    echo  [OK] Windows App SDK gia' presente
)

:: ── Avvia SysSuite One ───────────────────────────────────────
echo.
echo  [2/2] Avvio SysSuite One...
echo.

if exist "%~dp0SysSuite.exe" (
    start "" "%~dp0SysSuite.exe"
) else if exist "%~dp0..\SysSuite.exe" (
    start "" "%~dp0..\SysSuite.exe"
) else (
    echo  [ERRORE] SysSuite.exe non trovato.
    echo  Assicurati che INSTALLA_E_AVVIA.bat sia nella stessa cartella di SysSuite.exe
    pause
    exit /b 1
)

echo  [OK] SysSuite One avviato!
echo.
timeout /t 2 >nul
exit /b 0