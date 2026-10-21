param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$CodesignKey = "",
    [string]$CodesignProvision = ""
)

$ErrorActionPreference = "Stop"

if (-not $IsMacOS) {
    throw "La IPA de iOS debe compilarse en macOS con Xcode. Copiá o cloná el proyecto en un Mac y ejecutá este script allí."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repoRoot ".dotnet/dotnet"
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

$project = Join-Path $repoRoot "src/DndCompendium.App/DndCompendium.App.csproj"
$arguments = @(
    "publish",
    $project,
    "-f", "net10.0-ios",
    "-c", $Configuration,
    "-p:RuntimeIdentifier=ios-arm64",
    "-p:ArchiveOnBuild=true",
    "-p:BuildIpa=true"
)

if ($CodesignKey) {
    $arguments += "-p:CodesignKey=$CodesignKey"
}

if ($CodesignProvision) {
    $arguments += "-p:CodesignProvision=$CodesignProvision"
}

& $dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$output = Join-Path $repoRoot "src/DndCompendium.App/bin/$Configuration/net10.0-ios/ios-arm64/publish"
Write-Host "Publicación iOS terminada. Revisá la IPA en: $output"
