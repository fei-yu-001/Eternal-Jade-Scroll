# Start the verified current Windows client, independent of old Unity sessions.
[CmdletBinding()]
param(
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$windowsDirectory = Join-Path $projectRoot 'GameClient/TwelveJade/Builds/Windows'
$stampPath = Join-Path $projectRoot 'GameClient/TwelveJade/Assets/Resources/Config/build-stamp.txt'

if (-not (Test-Path -LiteralPath $stampPath -PathType Leaf)) {
    throw "Project build stamp is missing: $stampPath. Cannot verify the Windows client."
}
$expectedStamp = Get-Content -LiteralPath $stampPath -Raw
if ([string]::IsNullOrWhiteSpace($expectedStamp)) {
    throw "Project build stamp is empty: $stampPath. Cannot verify the Windows client."
}
$expectedStamp = $expectedStamp.Trim()
# A stamp is one Windows directory component, never a path or a reserved device name.
if ($expectedStamp -cnotmatch '\A[A-Za-z0-9][A-Za-z0-9._-]{0,254}\z' -or
    $expectedStamp.EndsWith('.') -or
    $expectedStamp -imatch '\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|\z)') {
    throw "Project build stamp is not a safe single directory name: $stampPath. Cannot verify the Windows client."
}

$versionDirectory = Join-Path $windowsDirectory $expectedStamp
$clientDirectory = $windowsDirectory
if (Test-Path -LiteralPath $versionDirectory) {
    if (-not (Test-Path -LiteralPath $versionDirectory -PathType Container)) {
        throw "Versioned Windows build path is not a directory: $versionDirectory. Rebuild the current client before launching."
    }
    # An existing version directory owns this stamp; failed validation must not fall back to an older build.
    $clientDirectory = $versionDirectory
}
$clientExecutable = Join-Path $clientDirectory 'TwelveJade.exe'
$verificationPath = Join-Path $clientDirectory 'build-verification.txt'
if (-not (Test-Path -LiteralPath $clientExecutable -PathType Leaf)) {
    throw "Current Windows build is missing: $clientExecutable. Rebuild the current client before launching."
}
if (-not (Test-Path -LiteralPath $verificationPath -PathType Leaf)) {
    throw "Windows build verification is missing: $verificationPath. Rebuild the current client before launching."
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
