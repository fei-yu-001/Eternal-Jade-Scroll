# TwelveJade Server MySQL verification script (W3-B / T024+T102).
# Proves on a REAL MySQL 8 instance that the UTC semantics of updatedAt hold end to end.
# Four steps, each aborts with "what is missing / how to fix it" on failure. It NEVER
# installs a database, never changes ports, and never falls back to H2 to fake a pass.
#
#   [1] prerequisites   - jar + mysql CLI + TWELVEJADE_DB_URL/_USERNAME/_PASSWORD + connectivity
#   [2] schema & probe  - run Database/SQL/V1__user_cloud_save.sql, then read back the actual
#                         type/nullability of users.token_at, saves.schema_version, saves.updated_at
#                         from information_schema and print the comparison table
#   [3] boot validation - start the jar with --spring.profiles.active=mysql; require
#                         ddl-auto: validate to pass AND the "UTC semantics check PASSED" log line
#                         from MysqlUtcSemanticsCheck (if it were skipped, that line is absent -> FAIL)
#   [4] end-to-end TZ   - register a temp user -> upload -> download/list; all three updatedAt
#                         values must be verbatim equal and end with Z; then SELECT updated_at
#                         straight from the DB and require the stored wall-clock literal to equal
#                         the UTC value the API reported (proof the driver does no hidden conversion)
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\verify-mysql.ps1
# Prerequisites: ./mvnw -B package has produced target\game-server-0.0.1.jar;
#                mysql CLI on PATH; TWELVEJADE_DB_URL / TWELVEJADE_DB_USERNAME / TWELVEJADE_DB_PASSWORD set
#                (see Database\SQL\MySQL接入手册.md; scripts\docker-compose.mysql.yml can provide the instance).
# Note: ASCII-only output (PS 5.1 reads BOM-less files as ANSI); mysql password is passed
#       through the MYSQL_PWD environment variable, never on a command line.

param(
    [string]$JarPath = (Join-Path $PSScriptRoot "..\target\game-server-0.0.1.jar"),
    [int]$Port = 18081
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Base = "http://127.0.0.1:$Port"
$SqlPath = Join-Path $PSScriptRoot "..\..\Database\SQL\V1__user_cloud_save.sql"

function Abort {
    param([string]$Step, [string]$What, [string]$HowToFix)
    Write-Host ("[ABORT] [{0}] {1}" -f $Step, $What)
    Write-Host ("        -> how to fix: {0}" -f $HowToFix)
    exit 1
}

# ---------- [1] prerequisites ----------
$step = "1/4 prerequisites"
if (-not (Test-Path $JarPath)) {
    Abort $step "jar not found: $JarPath" "run: cd GameServer; .\mvnw.cmd -B package -DskipTests"
}
$mysqlCli = Get-Command mysql -ErrorAction SilentlyContinue
if (-not $mysqlCli) {
    Abort $step "mysql CLI not found on PATH" "install MySQL 8 client tools (mysql.exe) and add them to PATH; the script will NOT install a database for you"
}
if (-not $env:TWELVEJADE_DB_URL) {
    Abort $step "TWELVEJADE_DB_URL is not set" "get a MySQL 8 instance, then set TWELVEJADE_DB_URL / TWELVEJADE_DB_USERNAME / TWELVEJADE_DB_PASSWORD (examples in Database/SQL/ - the MySQL integration manual - or: docker compose -f scripts/docker-compose.mysql.yml up -d)"
}
if (-not $env:TWELVEJADE_DB_USERNAME) {
    Abort $step "TWELVEJADE_DB_USERNAME is not set" "set TWELVEJADE_DB_USERNAME to the database account"
}
if (-not $env:TWELVEJADE_DB_PASSWORD) {
    Abort $step "TWELVEJADE_DB_PASSWORD is not set" "set TWELVEJADE_DB_PASSWORD (no default by design)"
}
if ($env:TWELVEJADE_DB_URL -notmatch 'jdbc:mysql://([^:/]+):(\d+)/([^?]+)') {
    Abort $step "cannot parse TWELVEJADE_DB_URL: $env:TWELVEJADE_DB_URL" "expected shape: jdbc:mysql://<host>:<port>/<database>?useSSL=false&connectionTimeZone=UTC"
}
$dbHost = $Matches[1]; $dbPort = $Matches[2]; $dbName = $Matches[3].Trim()
if ($env:TWELVEJADE_DB_URL -notmatch 'connectionTimeZone=UTC') {
    Write-Host "[WARN] [1/4] TWELVEJADE_DB_URL does not carry connectionTimeZone=UTC explicitly."
    Write-Host "        The hikari data-source-properties in application-mysql.yml inject it anyway;"
    Write-Host "        step [3] will prove it. If boot fails, add connectionTimeZone=UTC to the URL."
}

function Invoke-MySql {
    param([string]$Query, [string]$FeedFile)
    $env:MYSQL_PWD = $env:TWELVEJADE_DB_PASSWORD
    try {
        $args = @("--host=$dbHost", "--port=$dbPort",
            "--user=$env:TWELVEJADE_DB_USERNAME", "--default-character-set=utf8mb4",
            "--batch", "--raw")
        if ($FeedFile) {
            # native stdin redirect via cmd, so PS 5.1 does not re-encode the SQL file
            $argLine = ($args -join " ") + " $dbName < `"$FeedFile`""
            $out = cmd /c "mysql $argLine" 2>&1
        } else {
            $out = (& mysql @args -e $Query $dbName) 2>&1
        }
        if ($LASTEXITCODE -ne 0) {
            throw "mysql exited with $LASTEXITCODE : $out"
        }
        return $out
    } finally {
        Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue
    }
}

try {
    $version = (Invoke-MySql -Query "SELECT VERSION()") -join ""
} catch {
    Abort $step ("cannot reach MySQL at {0}:{1} ({2})" -f $dbHost, $dbPort, $_.Exception.Message) `
        "start a MySQL 8 instance reachable at that address (docker compose -f scripts/docker-compose.mysql.yml up -d) and re-run; this script will NOT install a database or change ports"
}
Write-Host ("[PASS] [1/4] prerequisites: jar ok, mysql CLI ok, server {0}:{1} reachable (MySQL {2}), database '{3}'" -f $dbHost, $dbPort, $version, $dbName)
if ($version -notmatch '^8\.') {
    Abort $step "server version is $version, expected MySQL 8.x" "point TWELVEJADE_DB_URL at a MySQL 8.0+ instance"
}

# ---------- [2] schema & probe ----------
$step = "2/4 schema"
if (-not (Test-Path $SqlPath)) {
    Abort $step "V1 script not found: $SqlPath" "run from the repository root layout (Database/SQL/V1__user_cloud_save.sql must exist)"
}
try {
    Invoke-MySql -FeedFile (Resolve-Path $SqlPath).Path | Out-Null
} catch {
    Abort $step "executing V1__user_cloud_save.sql failed: $($_.Exception.Message)" "check the MySQL error above (credentials / CREATE privileges on database '$dbName'); the script never edits the DDL"
}
$rows = Invoke-MySql -Query ("SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE FROM information_schema.COLUMNS " +
    "WHERE TABLE_SCHEMA='$dbName' AND ((TABLE_NAME='users' AND COLUMN_NAME='token_at') " +
    "OR (TABLE_NAME='saves' AND COLUMN_NAME IN ('schema_version','updated_at'))) ORDER BY TABLE_NAME, COLUMN_NAME")
$found = @{}
foreach ($line in $rows) {
    $p = "$line" -split "`t"
    if ($p.Count -ge 4) { $found["$($p[0]).$($p[1])"] = @($p[2], $p[3]) }
}
Write-Host "== actual column types from information_schema =="
Write-Host ("  users.token_at        -> {0}  nullable={1}" -f $found["users.token_at"][0], $found["users.token_at"][1])
Write-Host ("  saves.schema_version  -> {0}  nullable={1}" -f $found["saves.schema_version"][0], $found["saves.schema_version"][1])
Write-Host ("  saves.updated_at      -> {0}  nullable={1}" -f $found["saves.updated_at"][0], $found["saves.updated_at"][1])
$expected = @{
    "users.token_at"       = @("datetime(6)", "YES");
    "saves.schema_version" = @("int", "NO");
    "saves.updated_at"     = @("datetime(6)", "NO")
}
foreach ($key in $expected.Keys) {
    if (-not $found[$key]) {
        Abort $step "column $key not found in information_schema" "V1 ran without error but the column is missing - check that TABLE_SCHEMA '$dbName' is the database the URL points at"
    }
    if ("$($found[$key][0])".ToLowerInvariant() -ne $expected[$key][0] -or "$($found[$key][1])".ToUpperInvariant() -ne $expected[$key][1]) {
        Abort $step ("column {0} is {1}/{2}, expected {3}/{4}" -f $key, $found[$key][0], $found[$key][1], $expected[$key][0], $expected[$key][1]) `
            "the deployed schema drifted from V1 - fix the database or ship a V2 script; this script never edits the DDL"
    }
}
Write-Host "[PASS] [2/4] schema: V1 executed, 3 key columns match V1 (datetime(6) / int, nullability ok)"

# ---------- [3] boot validation ----------
$step = "3/4 boot"
$client = New-Object Net.Sockets.TcpClient
try { $client.Connect("127.0.0.1", $Port); $busy = $client.Connected } catch { $busy = $false } finally { $client.Close() }
if ($busy) {
    Abort $step "port $Port already in use" "stop whatever listens on $Port first (the script never changes ports)"
}
$outLog = Join-Path $env:TEMP "twelvejade-verify-mysql-out.log"
$errLog = Join-Path $env:TEMP "twelvejade-verify-mysql-err.log"
Remove-Item $outLog, $errLog -ErrorAction SilentlyContinue
# PS 5.1 Start-Process does not quote ArgumentList: wrap the jar path (contains spaces) ourselves
$server = Start-Process -FilePath "java" `
    -ArgumentList @("-jar", ('"{0}"' -f (Resolve-Path $JarPath).Path), "--spring.profiles.active=mysql", "--server.port=$Port") `
    -PassThru -WindowStyle Hidden -RedirectStandardOutput $outLog -RedirectStandardError $errLog

function Wait-Healthy {
    foreach ($i in 1..60) {
        Start-Sleep -Milliseconds 800
        $h = (& curl.exe -s "$Base/api/health") -join ""
        if ($h -match '"code":0') { return $true }
        if ($server.HasExited) { return $false }
    }
    return $false
}
try {
    if (-not (Wait-Healthy)) {
        $tail = (Get-Content $outLog -ErrorAction SilentlyContinue | Select-Object -Last 15) -join "`n"
        $errTail = (Get-Content $errLog -ErrorAction SilentlyContinue | Select-Object -Last 15) -join "`n"
        Abort $step "jar did not become healthy on mysql profile (pid $($server.Id))" `
            "see boot log below; usual causes: ddl-auto validate mismatch (schema drifted from V1), UTC semantics check refused a non-UTC connectionTimeZone, or wrong credentials.`n---- boot log tail ----`n$tail`n$errTail"
    }
    $bootLog = (Get-Content $outLog -Raw)
    if ($bootLog -notmatch 'profile is active[^\r\n]*"mysql"') {
        Abort $step "mysql profile does not appear active in the boot log" "the jar was started with --spring.profiles.active=mysql; if you passed a different override something rewrote it - do not downgrade to H2"
    }
    if ($bootLog -notmatch 'UTC semantics check PASSED') {
        Abort $step "MysqlUtcSemanticsCheck success line absent from boot log" `
            "the check is @Profile('mysql') only; it ran during boot on this profile, so a healthy start without the line means the log was filtered or the build is stale - rebuild the jar (mvnw -B package) and re-run"
    }
    Write-Host "[PASS] [3/4] boot: mysql profile active, ddl-auto=validate passed, 'UTC semantics check PASSED' found in boot log"

    # ---------- [4] end-to-end timezone assertion ----------
    $step = "4/4 end-to-end"
    $stamp = Get-Date -Format "yyyyMMddHHmmss"
    $user = "w3verify$stamp"
    $reg = (& curl.exe -s -X POST "$Base/api/auth/register" -H "Content-Type: application/json" `
        -d ('{\"username\":\"' + $user + '\",\"password\":\"verify1234\"}')) -join ""
    $regObj = $reg | ConvertFrom-Json
    if ($regObj.code -ne 0 -or -not $regObj.data.token) {
        Abort $step "register failed: $reg" "check the server log; on a reused database pick a fresh user name (registration data persists, unlike H2)"
    }
    $token = $regObj.data.token
    $userId = $regObj.data.userId
    $up = (& curl.exe -s -X POST "$Base/api/save/upload" -H "X-Auth-Token: $token" -H "Content-Type: application/json" `
        -d '{\"slot\":1,\"schemaVersion\":10,\"save\":{\"coins\":1024}}') -join ""
    $upObj = $up | ConvertFrom-Json
    if ($upObj.code -ne 0) { Abort $step "upload failed: $up" "see server log" }
    $dl = (& curl.exe -s "$Base/api/save/download?slot=1" -H "X-Auth-Token: $token") -join ""
    $ls = (& curl.exe -s "$Base/api/save/list" -H "X-Auth-Token: $token") -join ""
    $dlObj = $dl | ConvertFrom-Json
    $lsObj = $ls | ConvertFrom-Json
    $receipt = [string]$upObj.data.updatedAt
    $download = [string]$dlObj.data.updatedAt
    $listing = [string]$lsObj.data.slots[0].updatedAt
    Write-Host ("  receipt updatedAt   : {0}" -f $receipt)
    Write-Host ("  download updatedAt  : {0}" -f $download)
    Write-Host ("  list     updatedAt  : {0}" -f $listing)
    if ($receipt -notmatch 'Z$') { Abort $step "receipt updatedAt does not end with Z: $receipt" "the API must emit offset-carrying ISO8601 (W-Server-Time contract)" }
    if ($receipt -ne $download -or $receipt -ne $listing) {
        Abort $step "updatedAt differs across receipt/download/list" "same-second values must be verbatim equal (W-Server-Time contract)"
    }
    $dbRows = Invoke-MySql -Query ("SELECT updated_at FROM saves WHERE user_id={0} AND slot=1" -f $userId)
    $dbLiteral = ("$($dbRows | Select-Object -Last 1)").Trim()
    Write-Host ("  DB wall-clock literal (SELECT updated_at): {0}" -f $dbLiteral)
    # receipt '2026-10-05T12:09:33.69094Z'  vs  DB '2026-10-05 12:09:33.690940'
    # normalize both to 'YYYY-MM-DD HH:mm:SS.ffffff' (fraction zero-padded to 6) and compare
    function Normalize-Time([string]$t) {
        if ($t -match '^(\d{4}-\d{2}-\d{2})[T ](\d{2}:\d{2}:\d{2})(?:\.(\d+))?$') {
            $frac = "000000"
            if ($Matches[3]) { $frac = $Matches[3].PadRight(6, "0") }
            return ("{0} {1}.{2}" -f $Matches[1], $Matches[2], $frac)
        }
        return $t
    }
    $apiNorm = Normalize-Time ($receipt.TrimEnd("Z") -replace "T", " ")
    $dbNorm = Normalize-Time $dbLiteral
    if ($apiNorm -ne $dbNorm) {
        Abort $step ("wall-clock literal mismatch: API(UTC)='{0}' DB='{1}'" -f $apiNorm, $dbNorm) `
            "the driver performed a hidden timezone conversion - connectionTimeZone is not UTC; add connectionTimeZone=UTC to TWELVEJADE_DB_URL and re-run"
    }
    Write-Host ("[PASS] [4/4] end-to-end: three updatedAt verbatim equal & Z-suffixed; DB literal '{0}' == UTC wall time of the API" -f $dbLiteral)
    Write-Host "== MYSQL VERIFY PASS: 4/4 steps (T102 proven on real MySQL) =="
    exit 0
}
finally {
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue }
}
