param(
    [string]$Dotnet = 'dotnet',
    [string]$OutputArchive
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = if ($OutputArchive) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputArchive) }
    else { Join-Path $repo 'artifacts/Drivebrew-AppContainer-x64.msix' }
$stage = Join-Path $repo "artifacts/msix/$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $stage -Force
$project = Join-Path $repo 'src/DndCompendium.App/DndCompendium.App.csproj'
& $Dotnet publish $project -c Release -f net10.0-windows10.0.19041.0 `
    -p:BuildWindowsDesktop=true -p:BuildAppContainer=true -p:RuntimeIdentifierOverride=win-x64 `
    -p:Platform=x64 -p:SelfContained=true -p:AppxBundle=Never -p:NoWarn=MVVMTK0045 `
    "-p:AppxPackageDir=$stage/"
if ($LASTEXITCODE -ne 0) { throw "Falló la compilación del MSIX ($LASTEXITCODE)." }
$package = Get-ChildItem -LiteralPath $stage -Filter '*.msix' -Recurse | Select-Object -First 1
if (!$package) { throw 'No se generó el MSIX.' }
$zip = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $entry = $zip.GetEntry('AppxManifest.xml')
    $reader = [IO.StreamReader]::new($entry.Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $application = $manifest.Package.Applications.Application
    $trustNamespace = 'http://schemas.microsoft.com/appx/manifest/uap/windows10/10'
    if ($application.GetAttribute('TrustLevel', $trustNamespace) -ne 'appContainer' -or
        $manifest.Package.Capabilities.ChildNodes.Name -contains 'runFullTrust' -or
        $manifest.OuterXml -match 'Name="runFullTrust"') {
        throw 'El paquete resultante no respeta el aislamiento AppContainer.'
    }
    if ($zip.GetEntry('AppxSignature.p7x')) { throw 'El paquete de desarrollo debe quedar sin firma.' }
}
finally { $zip.Dispose() }
Copy-Item -LiteralPath $package.FullName -Destination $output -Force
Write-Output "MSIX sin firma, AppContainer validado: $output"
