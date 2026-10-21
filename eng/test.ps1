param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repoRoot ".dotnet\dotnet.exe"

if (-not (Test-Path -LiteralPath $localDotnet)) {
    throw "Falta el SDK local. Ejecutá eng/bootstrap.ps1."
}

$testProjects = @(
    (Join-Path $repoRoot "tests\DndCompendium.Core.Tests\DndCompendium.Core.Tests.csproj"),
    (Join-Path $repoRoot "tests\DndCompendium.Infrastructure.Tests\DndCompendium.Infrastructure.Tests.csproj")
)

foreach ($project in $testProjects) {
    & $localDotnet test $project -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
