param(
    [string]$SdkVersion = "10.0.303",
    [string]$Workload = "maui-mobile"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repoRoot ".dotnet\dotnet.exe"

if (-not (Test-Path -LiteralPath $localDotnet)) {
    $installer = Join-Path ([IO.Path]::GetTempPath()) "dotnet-install-dnd-compendium.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installer
    & powershell -NoProfile -ExecutionPolicy Bypass -File $installer `
        -Version $SdkVersion `
        -InstallDir (Join-Path $repoRoot ".dotnet") `
        -NoPath
}

& $localDotnet workload install $Workload --verbosity minimal

$jdkRoot = Join-Path $repoRoot ".tools\jdk-21.0.12.1+1"
if (-not (Test-Path -LiteralPath (Join-Path $jdkRoot "bin\java.exe"))) {
    $jdkArchive = Join-Path ([IO.Path]::GetTempPath()) "microsoft-jdk-21.0.12.1-windows-x64.zip"
    Invoke-WebRequest `
        -Uri "https://aka.ms/download-jdk/microsoft-jdk-21.0.12.1-windows-x64.zip" `
        -OutFile $jdkArchive
    New-Item -ItemType Directory -Path (Join-Path $repoRoot ".tools") -Force | Out-Null
    Expand-Archive -LiteralPath $jdkArchive -DestinationPath (Join-Path $repoRoot ".tools") -Force
}

Write-Host "Entorno local preparado. Android SDK: configuralo con Android Studio si aún no existe."
