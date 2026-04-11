# ============================================================
#  SysSuite One - Installer Windows App SDK
#  Scarica e installa silenziosamente il runtime necessario
# ============================================================
param([switch]$Silent)

$SDK_VERSION = "1.6.3"
$SDK_URL     = "https://aka.ms/windowsappsdk/1.6/latest/windowsappruntimeinstall-x64.exe"
$SDK_FILE    = "$env:TEMP\WindowsAppRuntimeInstall-x64.exe"

function Write-Step {
    param([string]$Msg, [string]$Color = "Cyan")
    Write-Host "  >> $Msg" -ForegroundColor $Color
}

Write-Host ""
Write-Host "  SysSuite One - Prerequisiti" -ForegroundColor Cyan
Write-Host "  =================================" -ForegroundColor DarkCyan
Write-Host ""

# Controlla se Windows App SDK e' gia' installato
function Test-WindowsAppSDK {
    try {
        $pkg = Get-AppxPackage -Name "Microsoft.WindowsAppRuntime*" -ErrorAction SilentlyContinue
        return $pkg -ne $null
    } catch { return $false }
}

if (Test-WindowsAppSDK) {
    Write-Step "Windows App SDK gia' installato - OK" "Green"
    exit 0
}

Write-Step "Windows App SDK non trovato. Download in corso..."
Write-Step "URL: $SDK_URL"
Write-Host ""

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Write-Step "Download in corso (salvataggio: $SDK_FILE)..."
    Invoke-WebRequest -Uri $SDK_URL -OutFile $SDK_FILE -UseBasicParsing
    Write-Step "Download completato" "Green"

    # Installa (silenzioso se -Silent, interattivo altrimenti)
    Write-Step "Installazione in corso..."
    $args = if ($Silent) { "--quiet" } else { "" }
    $proc = Start-Process -FilePath $SDK_FILE -ArgumentList $args -Wait -PassThru

    if ($proc.ExitCode -eq 0) {
        Write-Step "Windows App SDK installato correttamente!" "Green"
    } else {
        Write-Step "Installazione completata con codice: $($proc.ExitCode)" "Yellow"
    }

    Remove-Item $SDK_FILE -Force -ErrorAction SilentlyContinue

} catch {
    Write-Step "Errore download: $_" "Red"
    Write-Host ""
    Write-Host "  Scarica manualmente da:" -ForegroundColor Yellow
    Write-Host "  https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads" -ForegroundColor Yellow
    exit 1
}