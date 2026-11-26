param(
    [Parameter(Mandatory)][string]$ApkSigner,
    [Parameter(Mandatory)][string]$UnsignedApk,
    [Parameter(Mandatory)][string]$KeyStore,
    [Parameter(Mandatory)][string]$KeyAlias,
    [Parameter(Mandatory)][string]$ExpectedCertificateSha256,
    [Parameter(Mandatory)][string]$OutputApk
)

$ErrorActionPreference = 'Stop'
$expected = ($ExpectedCertificateSha256 -replace '[:-]', '').ToLowerInvariant()
if ($expected -notmatch '^[0-9a-f]{64}$') { throw 'La huella esperada debe ser SHA-256.' }
$signer = (Resolve-Path -LiteralPath $ApkSigner).Path
$inputApk = (Resolve-Path -LiteralPath $UnsignedApk).Path
$key = (Resolve-Path -LiteralPath $KeyStore).Path
$destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputApk)
if (Test-Path -LiteralPath $destination) { throw 'El archivo de salida ya existe.' }
$staged = Join-Path (Split-Path -Parent $destination) "$([Guid]::NewGuid().ToString('N')).apk"

try {
    & $signer sign --ks $key --ks-key-alias $KeyAlias --out $staged $inputApk
    if ($LASTEXITCODE -ne 0) { throw 'Falló la firma de Android.' }
    $verification = & $signer verify --print-certs $staged
    if ($LASTEXITCODE -ne 0) { throw 'Falló la verificación de la APK.' }
    $fingerprints = @($verification | Select-String '^Signer #\d+ certificate SHA-256 digest: ([0-9a-f]{64})$')
    if ($fingerprints.Count -ne 1 -or $fingerprints[0].Matches[0].Groups[1].Value -ne $expected) {
        throw 'La firma de la APK no coincide con el certificado esperado.'
    }
    Move-Item -LiteralPath $staged -Destination $destination
    Get-FileHash -LiteralPath $destination -Algorithm SHA256
}
finally {
    if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged }
    if (Test-Path -LiteralPath "$staged.idsig") { Remove-Item -LiteralPath "$staged.idsig" }
}
