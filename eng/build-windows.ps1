param(
    [string]$VersionName,
    [int]$VersionCode,
    [string]$Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src/DndCompendium.App/DndCompendium.App.csproj'
$folder = Join-Path $repo 'artifacts/windows-x64'
$zip = Join-Path $repo 'artifacts/DndCompendium-Windows-x64.zip'
$arguments = @(
    'publish', $project, '-c', 'Release', '-f', 'net10.0-windows10.0.19041.0',
    '-p:BuildWindowsDesktop=true', '-p:RuntimeIdentifierOverride=win-x64',
    '-p:SelfContained=true', '-p:NoWarn=MVVMTK0045'
)
if ($VersionName) { $arguments += "-p:ApplicationDisplayVersion=$VersionName" }
if ($VersionCode) { $arguments += "-p:ApplicationVersion=$VersionCode" }

# Stage into a fresh directory so old DLLs cannot enter a new release.
$staging = Join-Path (Join-Path $folder ([Guid]::NewGuid().ToString('N'))) 'DndCompendium'
& $Dotnet @arguments -o $staging
if ($LASTEXITCODE -ne 0) { throw "Windows publish failed ($LASTEXITCODE)." }
if (-not (Test-Path -LiteralPath (Join-Path $staging 'DndCompendium.exe'))) {
    throw 'DndCompendium.exe was not generated.'
}

Compress-Archive -LiteralPath $staging -DestinationPath $zip -Force
Write-Output "Executable: $(Join-Path $staging 'DndCompendium.exe')"
Write-Output "Archive: $zip"
