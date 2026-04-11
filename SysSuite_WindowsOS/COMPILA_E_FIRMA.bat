@echo off
setlocal

set "TRACE=%TEMP%\SysSuite_COMPILE_trace.txt"
echo [%DATE% %TIME%] Avvio script>"%TRACE%"

cd /d "%~dp0" 2>>"%TRACE%"
if errorlevel 1 (
    echo [ERRORE] Cartella non valida. Vedi: %TRACE%
    pause
    exit /b 1
)

set "ROOT=%CD%"
chcp 65001 >nul 2>&1
title SysSuite One - Build e Firma

if not exist "%ROOT%\dist\" mkdir "%ROOT%\dist\"

echo.
echo  =====================================================
echo   SYSSUITE ONE - Build e firma
echo  =====================================================
echo   Cartella: %ROOT%
echo  =====================================================
echo.

REM ==== [STEP 0] .NET SDK ====
set "DOTNET_CMD="
where dotnet >nul 2>&1
if not errorlevel 1 set "DOTNET_CMD=dotnet"
if not defined DOTNET_CMD (
    if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_CMD=%ProgramFiles%\dotnet\dotnet.exe"
)
if not defined DOTNET_CMD (
    if exist "%ProgramFiles(x86)%\dotnet\dotnet.exe" set "DOTNET_CMD=%ProgramFiles(x86)%\dotnet\dotnet.exe"
)
if not defined DOTNET_CMD (
    echo  [INFO] .NET SDK non nel PATH. Installo con winget...
    where winget >nul 2>&1
    if errorlevel 1 (
        echo  [ERRORE] Installa .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
        pause
        exit /b 1
    )
    winget install --id Microsoft.DotNet.SDK.8 --accept-package-agreements --accept-source-agreements --silent
    timeout /t 8 /nobreak >nul
    if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_CMD=%ProgramFiles%\dotnet\dotnet.exe"
)
if not defined DOTNET_CMD (
    echo  [ERRORE] .NET SDK non trovato.
    pause
    exit /b 1
)
echo  [OK] .NET SDK:
"%DOTNET_CMD%" --version
if errorlevel 1 ( pause & exit /b 1 )

REM ==== [STEP 1] Windows App Runtime ====
echo.
echo  [1/5] Windows App Runtime...
set "WSDK_PS="
if exist "%ROOT%\Installer\Install_WindowsAppSDK.ps1" set "WSDK_PS=%ROOT%\Installer\Install_WindowsAppSDK.ps1"
if not defined WSDK_PS (
    if exist "%ROOT%\SysSuite\Installer\Install_WindowsAppSDK.ps1" set "WSDK_PS=%ROOT%\SysSuite\Installer\Install_WindowsAppSDK.ps1"
)
if defined WSDK_PS (
    PowerShell -NoProfile -ExecutionPolicy Bypass -File "%WSDK_PS%" -Silent
    if errorlevel 1 ( echo  [AVVISO] Windows App Runtime non completato. ) else ( echo  [OK] Windows App Runtime verificato. )
) else (
    echo  [AVVISO] Install_WindowsAppSDK.ps1 non trovato.
)

REM ==== [STEP 1b] NuGet restore (popola cache PRIMA di cercare signtool) ====
set "CSPROJ="
if exist "%ROOT%\SysSuite.csproj" set "CSPROJ=%ROOT%\SysSuite.csproj"
if not defined CSPROJ (
    if exist "%ROOT%\SysSuite\SysSuite.csproj" set "CSPROJ=%ROOT%\SysSuite\SysSuite.csproj"
)
if not defined CSPROJ (
    echo  [ERRORE] SysSuite.csproj non trovato.
    pause
    exit /b 1
)
echo  [OK] Progetto: %CSPROJ%
echo.
echo  [1b/5] dotnet restore...
"%DOTNET_CMD%" restore "%CSPROJ%" -r win-x64 --nologo
if errorlevel 1 ( echo  [ERRORE] NuGet restore fallito. & pause & exit /b 1 )
echo  [OK] NuGet restore completato.

REM ==== [STEP 2] Certificato ====
echo.
set "CERT_PS="
if exist "%ROOT%\Installer\Create-CodeSignPfx.ps1" set "CERT_PS=%ROOT%\Installer\Create-CodeSignPfx.ps1"
if not defined CERT_PS (
    if exist "%ROOT%\SysSuite\Installer\Create-CodeSignPfx.ps1" set "CERT_PS=%ROOT%\SysSuite\Installer\Create-CodeSignPfx.ps1"
)
if not exist "%ROOT%\SysSuite_CodeSign.pfx" (
    echo  [2/5] Generazione certificato...
    if not defined CERT_PS ( echo  [ERRORE] Manca Create-CodeSignPfx.ps1 & pause & exit /b 1 )
    PowerShell -NoProfile -ExecutionPolicy Bypass -File "%CERT_PS%" -OutputRoot "%ROOT%"
    if errorlevel 1 ( pause & exit /b 1 )
    if not exist "%ROOT%\SysSuite_CodeSign.pfx" ( echo  [ERRORE] PFX non creato. & pause & exit /b 1 )
) else (
    echo  [2/5] Certificato gia' presente.
)

REM ==== [STEP 3] Pulizia dist ====
echo.
echo  [3/5] Pulizia dist\...
if exist "%ROOT%\dist\" rd /s /q "%ROOT%\dist\"
mkdir "%ROOT%\dist\"
set "PUBLISH_LOG=%ROOT%\dist\_publish_last.log"

REM ==== WindowsSdkPath: registro -> Program Files -> NuGet cache ====
set "WINSDK_PATH="
set "NUGET_CACHE=%USERPROFILE%\.nuget\packages"
if defined NUGET_PACKAGES set "NUGET_CACHE=%NUGET_PACKAGES%"

for /f "usebackq delims=" %%i in (`powershell -NoProfile -Command "try{(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' -EA Stop).KitsRoot10}catch{''}"`) do set "WINSDK_PATH=%%i"
if not defined WINSDK_PATH (
    if exist "%ProgramFiles(x86)%\Windows Kits\10\Platforms.xml" set "WINSDK_PATH=%ProgramFiles(x86)%\Windows Kits\10\"
)
if not defined WINSDK_PATH (
    if exist "%ProgramFiles%\Windows Kits\10\Platforms.xml" set "WINSDK_PATH=%ProgramFiles%\Windows Kits\10\"
)
if not defined WINSDK_PATH (
    for /d %%v in ("%NUGET_CACHE%\microsoft.windows.sdk.buildtools\10.0.*") do set "WINSDK_PATH=%%v\"
    if defined WINSDK_PATH echo  [OK] WindowsSdkPath via NuGet BuildTools.
)
if defined WINSDK_PATH (
    echo  [OK] WindowsSdkPath=%WINSDK_PATH%
    set "EXTRA_SDK=-p:WindowsSdkPath=%WINSDK_PATH%"
) else (
    echo  [INFO] WindowsSdkPath: MSBuild usa NuGet automaticamente.
    set "EXTRA_SDK="
)

REM ==== [STEP 4] dotnet publish ====
echo.
echo  [4/5] dotnet publish (puo richiedere 1-5 minuti)...
echo  Output: %ROOT%\dist
echo.

"%DOTNET_CMD%" publish "%CSPROJ%" -c Release -f net8.0-windows10.0.19041.0 -r win-x64 --self-contained true -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:AssemblyVersion=1.0.0.0 -p:FileVersion=1.0.0.0 -p:Company="Antonio Cardelli" -p:Copyright="Copyright 2025 Antonio Cardelli" -p:Product="SysSuite One" %EXTRA_SDK% -o "%ROOT%\dist" --verbosity normal --nologo 1> "%PUBLISH_LOG%" 2>&1

type "%PUBLISH_LOG%"

if not exist "%ROOT%\dist\SysSuite.exe" (
    echo.
    echo  [INFO] Riprovo con WindowsAppSDKSelfContained=false...
    "%DOTNET_CMD%" publish "%CSPROJ%" -c Release -f net8.0-windows10.0.19041.0 -r win-x64 --self-contained true -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=false -p:PublishSingleFile=false %EXTRA_SDK% -o "%ROOT%\dist" --verbosity normal --nologo 1>> "%PUBLISH_LOG%" 2>&1
    type "%PUBLISH_LOG%"
)

if not exist "%ROOT%\dist\SysSuite.exe" (
    echo.
    echo  [ERRORE] SysSuite.exe non creato. Controlla: dist\_publish_last.log
    pause
    exit /b 1
)

echo.
echo  [OK] Pubblicazione completata.

REM ==== [STEP 5] signtool (cercato DOPO publish; NuGet cache ora popolato) ====
echo.
echo  [5/5] Ricerca signtool...

set "SIGNTOOL="
where signtool >nul 2>&1
if not errorlevel 1 set "SIGNTOOL=signtool"
if not defined SIGNTOOL (
    for %%v in (10.0.26100.0 10.0.22621.0 10.0.19041.0) do (
        if exist "C:\Program Files (x86)\Windows Kits\10\bin\%%v\x64\signtool.exe" (
            set "SIGNTOOL=C:\Program Files (x86)\Windows Kits\10\bin\%%v\x64\signtool.exe"
        )
    )
)
if not defined SIGNTOOL (
    for /d %%v in ("%NUGET_CACHE%\microsoft.windows.sdk.buildtools\10.0.*") do (
        if exist "%%v\bin\10.0.26100.0\x64\signtool.exe" (
            set "SIGNTOOL=%%v\bin\10.0.26100.0\x64\signtool.exe"
        )
    )
    if defined SIGNTOOL echo  [OK] signtool trovato nel cache NuGet BuildTools.
)
if not defined SIGNTOOL (
    echo  [AVVISO] signtool non trovato - firma saltata.
    echo  Per abilitare: winget install Microsoft.WindowsSDK.10.0.26100
    goto build_done
)
echo  [OK] signtool: %SIGNTOOL%

REM ---- FIRMA ----
REM NOTA: la password contiene "!" — con EnableDelayedExpansion attivo, CMD
REM       consumerebbe tutto dopo "!" come nome di variabile dinamica, troncando
REM       /fd SHA256 e il filename. Usiamo un subshell senza delayed expansion.
set "SIGN_EXE=%SIGNTOOL%"
set "SIGN_PFX=%ROOT%\SysSuite_CodeSign.pfx"
set "SIGN_OUT=%ROOT%\dist\SysSuite.exe"

echo  Firma digitale in corso...
REM cmd /v:off esegue il comando con delayed expansion DISABILITATA
cmd /v:off /c ""%SIGN_EXE%" sign /f "%SIGN_PFX%" /p "SysSuite2024!" /fd SHA256 /tr "http://timestamp.digicert.com" /td SHA256 /d "SysSuite One" /v "%SIGN_OUT%""
if errorlevel 1 (
    echo  [AVVISO] Timestamp remoto fallito. Riprovo senza timestamp...
    cmd /v:off /c ""%SIGN_EXE%" sign /f "%SIGN_PFX%" /p "SysSuite2024!" /fd SHA256 /d "SysSuite One" /v "%SIGN_OUT%""
)
if not errorlevel 1 (
    echo  [OK] Firma applicata con successo.
) else (
    echo  [AVVISO] Firma fallita.
)

:build_done
echo.
echo  =====================================================
echo   BUILD COMPLETATA
echo   Output: %ROOT%\dist
echo  =====================================================
echo.
PowerShell -NoProfile -Command "try{$p='%ROOT%\dist\SysSuite.exe';$s=Get-AuthenticodeSignature -LiteralPath $p;Write-Host('   Firma: '+$s.Status)}catch{}"
echo.
explorer.exe "%ROOT%\dist"
pause
endlocal
exit /b 0
