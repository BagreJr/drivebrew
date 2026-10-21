param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repoRoot ".dotnet\dotnet.exe"
$appProject = Join-Path $repoRoot "src\DndCompendium.App\DndCompendium.App.csproj"

if (-not (Test-Path -LiteralPath $localDotnet)) {
    throw "Falta el SDK local. Ejecutá eng/bootstrap.ps1."
}

$androidSdkCandidates = @(
    $env:ANDROID_SDK_ROOT,
    $env:ANDROID_HOME,
    "D:\Android App",
    (Join-Path $env:LOCALAPPDATA "Android\Sdk")
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

$androidSdk = $androidSdkCandidates | Select-Object -First 1
if (-not $androidSdk) {
    throw "No se encontró el Android SDK. Instalalo desde Android Studio o definí ANDROID_SDK_ROOT."
}

$requiredAndroidJar = Join-Path $androidSdk "platforms\android-36\android.jar"
if (-not (Test-Path -LiteralPath $requiredAndroidJar)) {
    throw "Falta Android SDK Platform 36. Instalalo desde Android Studio > SDK Manager (activá Show Package Details si solo aparece 36.1)."
}

$jdkCandidates = @(
    $env:JAVA_HOME,
    (Join-Path $repoRoot ".tools\jdk-21.0.12.1+1"),
    "C:\Program Files\Microsoft\jdk-21.0.12.1-hotspot"
) | Where-Object { $_ -and (Test-Path -LiteralPath (Join-Path $_ "bin\java.exe")) }

$javaSdk = $jdkCandidates | Select-Object -First 1
if (-not $javaSdk) {
    throw "No se encontró JDK 21. Ejecutá eng/bootstrap.ps1."
}

& $localDotnet build $appProject `
    --no-incremental `
    -f net10.0-android `
    -c $Configuration `
    -p:AndroidSdkDirectory="$androidSdk" `
    -p:JavaSdkDirectory="$javaSdk" `
    -p:EmbedAssembliesIntoApk=true

exit $LASTEXITCODE
