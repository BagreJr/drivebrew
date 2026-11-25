param(
    [string]$VersionName,
    [int]$VersionCode,
    [string]$Dotnet = 'dotnet',
    [string]$PublishedAppDirectory,
    [string]$OutputArchive
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src/DndCompendium.App/DndCompendium.App.csproj'
$folder = Join-Path $repo 'artifacts/windows-x64'
$zip = if ($OutputArchive) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputArchive) }
    else { Join-Path $repo 'artifacts/DndCompendium-Windows-x64.zip' }
$arguments = @(
    'publish', $project, '-c', 'Release', '-f', 'net10.0-windows10.0.19041.0',
    '-p:BuildWindowsDesktop=true', '-p:RuntimeIdentifierOverride=win-x64',
    '-p:SelfContained=true', '-p:NoWarn=MVVMTK0045'
)
if ($VersionName) { $arguments += "-p:ApplicationDisplayVersion=$VersionName" }
if ($VersionCode) { $arguments += "-p:ApplicationVersion=$VersionCode" }

# Stage into a fresh directory so old DLLs cannot enter a new release.
$buildDirectory = Join-Path $folder ([Guid]::NewGuid().ToString('N'))
$staging = Join-Path $buildDirectory 'DndCompendium'
$appDirectory = Join-Path $staging 'App'
$null = New-Item -Path $staging -ItemType Directory -Force
if ($PublishedAppDirectory) {
    Copy-Item -LiteralPath (Resolve-Path -LiteralPath $PublishedAppDirectory).Path -Destination $appDirectory -Recurse
}
else {
    & $Dotnet @arguments -o $appDirectory
    if ($LASTEXITCODE -ne 0) { throw "Windows publish failed ($LASTEXITCODE)." }
}
$appExecutable = Join-Path $appDirectory 'DndCompendium.exe'
if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) {
    throw 'DndCompendium.exe was not generated.'
}

# The .NET Framework launcher uses the runtime included with supported versions of Windows.
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$launcher = Join-Path $staging 'DndCompendium.exe'
$iconPath = Join-Path $buildDirectory 'DndCompendium.ico'
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::ExtractAssociatedIcon($appExecutable)
$launcherArguments = @('/nologo', '/target:winexe', '/platform:x64', '/reference:System.Windows.Forms.dll', "/out:$launcher")
if ($icon) {
    try {
        $iconFile = [IO.File]::Create($iconPath)
        try { $icon.Save($iconFile) } finally { $iconFile.Dispose() }
    }
    finally { $icon.Dispose() }
    $launcherArguments += "/win32icon:$iconPath"
}
& $compiler @launcherArguments (Join-Path $PSScriptRoot 'WindowsLauncher.cs')
if ($LASTEXITCODE -ne 0) { throw "Windows launcher build failed ($LASTEXITCODE)." }

Compress-Archive -LiteralPath $staging -DestinationPath $zip -Force
Write-Output "Executable: $launcher"
Write-Output "Archive: $zip"
