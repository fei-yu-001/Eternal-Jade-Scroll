# Start the verified current Windows client, independent of old Unity sessions.
[CmdletBinding()]
param(
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$clientDirectory = Join-Path $projectRoot 'GameClient/TwelveJade/Builds/Windows'
$clientExecutable = Join-Path $clientDirectory 'TwelveJade.exe'
$stampPath = Join-Path $projectRoot 'GameClient/TwelveJade/Assets/Resources/Config/build-stamp.txt'
$verificationPath = Join-Path $clientDirectory 'build-verification.txt'

if (-not (Test-Path -LiteralPath $clientExecutable -PathType Leaf)) {
    throw "Current Windows build is missing: $clientExecutable. Rebuild the current client before launching."
}
if (-not (Test-Path -LiteralPath $stampPath -PathType Leaf)) {
    throw "Project build stamp is missing: $stampPath. Cannot verify the Windows client."
}
if (-not (Test-Path -LiteralPath $verificationPath -PathType Leaf)) {
    throw "Windows build verification is missing: $verificationPath. Rebuild the current client before launching."
}

$expectedStamp = ([string](Get-Content -LiteralPath $stampPath -Raw)).Trim()
if ([string]::IsNullOrWhiteSpace($expectedStamp)) {
    throw "Project build stamp is empty: $stampPath. Cannot verify the Windows client."
}
$verification = @{}
foreach ($line in Get-Content -LiteralPath $verificationPath) {
    if ($line -cmatch '^(Stamp|Result)=(.*)$') {
        $key = $Matches[1]
        if ($verification.ContainsKey($key)) {
            throw "Windows build verification contains duplicate ${key}: $verificationPath. Rebuild the current client before launching."
        }
        $verification[$key] = $Matches[2].Trim()
    }
}
foreach ($key in @('Stamp', 'Result')) {
    if ([string]::IsNullOrWhiteSpace($verification[$key])) {
        throw "Windows build verification has no ${key}: $verificationPath. Rebuild the current client before launching."
    }
}
if ($verification['Result'] -cne 'Succeeded') {
    throw "Windows build did not succeed (Result=$($verification['Result'])): $verificationPath. Rebuild the current client before launching."
}
if ($verification['Stamp'] -cne $expectedStamp) {
    throw "Windows build is out of date: expected Stamp=$expectedStamp, found Stamp=$($verification['Stamp']). Rebuild the current client before launching."
}

if ($ValidateOnly) {
    return [pscustomobject]@{
        Stamp = $expectedStamp
        Result = $verification['Result']
        Executable = $clientExecutable
        WorkingDirectory = $clientDirectory
    }
}
Start-Process -FilePath $clientExecutable -WorkingDirectory $clientDirectory -WindowStyle Normal
