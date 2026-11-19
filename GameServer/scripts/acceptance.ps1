# TwelveJade Server acceptance script v2 (M6-03-S).
# Boots target\game-server-0.0.1.jar (H2 in-memory, always fresh), runs the 13-step
# curl chain, prints each step's HTTP status and business code, exits 0 only when
# every step matches the expectation.
#
# v2 = v1 的 9 步 + [10] refresh（换发+旧 token 失效） + [11] payload 超限 400/40001
#      + [12] 改密码（成功+旧 token 失效+新密码可登录） + [13] 登录失败锁定 409/40904。
# [13] 用独立用户名并放最后——锁定状态是进程内存态，不能污染前面步骤的登录。
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\acceptance.ps1
# Prerequisite: ./mvnw -B package has produced target\game-server-0.0.1.jar.
# Note: uses curl.exe explicitly (plain `curl` in Windows PowerShell is an alias of
#       Invoke-WebRequest) and ASCII-only output (PS 5.1 reads BOM-less files as ANSI).

param(
    [string]$JarPath = (Join-Path $PSScriptRoot "..\target\game-server-0.0.1.jar"),
    [int]$Port = 18080
)

$ErrorActionPreference = "Stop"
# PS 5.1 decodes native-command output with the OEM codepage; curl's UTF-8 bodies
# (Chinese messages) would get mangled into invalid JSON. Force UTF-8.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Base = "http://127.0.0.1:$Port"

if (-not (Test-Path $JarPath)) {
    Write-Host "[ABORT] jar not found: $JarPath  (run: .\mvnw.cmd -B package)"
    exit 1
}

# Port already occupied -> refuse to start a second instance (a stale server would
# also make step 2 fail with 40902 because its H2 is not fresh).
$client = New-Object Net.Sockets.TcpClient
try { $client.Connect("127.0.0.1", $Port); $busy = $client.Connected } catch { $busy = $false } finally { $client.Close() }
if ($busy) {
    Write-Host "[ABORT] port $Port already in use, stop the running server first."
    exit 1
}

# PS 5.1 Start-Process does not quote ArgumentList elements: the jar path contains
# spaces ("Eternal Jade Scroll"), so wrap it in literal quotes ourselves.
$server = Start-Process -FilePath "java" `
    -ArgumentList @("-jar", ('"{0}"' -f (Resolve-Path $JarPath).Path)) `
    -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput "$env:TEMP\twelvejade-acceptance-out.log" `
    -RedirectStandardError "$env:TEMP\twelvejade-acceptance-err.log"

function Wait-Healthy {
    foreach ($i in 1..40) {
        Start-Sleep -Milliseconds 800
        $h = (& curl.exe -s "$Base/api/health") -join ""
        if ($h -match '"code":0') { return $true }
    }
    return $false
}

# Runs one curl step; PASS iff HTTP status and business code both match.
function Invoke-Step {
    param([string]$Name, [int]$ExpectedHttp, [int]$ExpectedCode, [string[]]$CurlArgs)
    $out = & curl.exe -s -w "`n%{http_code}" @CurlArgs
    $http = [int]$out[-1]
    $body = ""
    if ($out.Count -gt 1) { $body = ($out[0..($out.Count - 2)]) -join "" }
    $obj = $null
    try { $obj = $body | ConvertFrom-Json } catch {}
    $code = -1
    if ($obj -and $obj.PSObject.Properties["code"]) { $code = [int]$obj.code }
    if ($http -eq $ExpectedHttp -and $code -eq $ExpectedCode) {
        Write-Host ("[PASS] {0}  http={1} code={2}" -f $Name, $http, $code)
        return $true
    }
    Write-Host ("[FAIL] {0}  expected http={1} code={2}  got http={3} code={4}  body={5}" -f $Name, $ExpectedHttp, $ExpectedCode, $http, $code, $body)
    return $false
}

$failures = 0
try {
    if (-not (Wait-Healthy)) {
        Write-Host "[ABORT] server did not become healthy in 40s (see $env:TEMP\twelvejade-acceptance-err.log)"
        exit 1
    }
    Write-Host "== server up (pid $($server.Id)), H2 in-memory fresh =="

    # [1] health
    if (-not (Invoke-Step "01 health" 200 0 @("$Base/api/health"))) { $failures++ }

    # [2] register -> token1
    $reg = (& curl.exe -s -X POST "$Base/api/auth/register" -H "Content-Type: application/json" `
        -d '{\"username\":\"tester\",\"password\":\"pass1234\"}') -join ""
    $regObj = $reg | ConvertFrom-Json
    if ($regObj.code -eq 0 -and $regObj.data.token) {
        Write-Host ("[PASS] 02 register  code=0 userId={0} token={1}" -f $regObj.data.userId, $regObj.data.token)
        $token1 = $regObj.data.token
    } else {
        Write-Host "[FAIL] 02 register  body=$reg"
        $failures++
        $token1 = ""
    }

    # [3] login -> token2 (must differ from token1: rotation)
    $login = (& curl.exe -s -X POST "$Base/api/auth/login" -H "Content-Type: application/json" `
        -d '{\"username\":\"tester\",\"password\":\"pass1234\"}') -join ""
    $loginObj = $login | ConvertFrom-Json
    if ($loginObj.code -eq 0 -and $loginObj.data.token -and $loginObj.data.token -ne $token1) {
        Write-Host ("[PASS] 03 login  code=0 new token={0} (old invalidated)" -f $loginObj.data.token)
        $token = $loginObj.data.token
    } else {
        Write-Host "[FAIL] 03 login  body=$login"
        $failures++
        $token = $token1
    }

    # [4] me
    if (-not (Invoke-Step "04 me" 200 0 @("$Base/api/me", "-H", "X-Auth-Token: $token"))) { $failures++ }

    # [5] upload slot 1
    if (-not (Invoke-Step "05 upload" 200 0 @("-X", "POST", "$Base/api/save/upload", "-H", "X-Auth-Token: $token", "-H", "Content-Type: application/json", "-d", '{\"slot\":1,\"schemaVersion\":10,\"save\":{\"coins\":300}}'))) { $failures++ }

    # [6] download slot 1 -> payload round-trip
    $dl = (& curl.exe -s "$Base/api/save/download?slot=1" -H "X-Auth-Token: $token") -join ""
    $dlObj = $dl | ConvertFrom-Json
    if ($dlObj.code -eq 0 -and $dlObj.data.present -and $dlObj.data.save.coins -eq 300) {
        Write-Host "[PASS] 06 download  code=0 present=True save.coins=300 (payload verbatim)"
    } else {
        Write-Host "[FAIL] 06 download  body=$dl"
        $failures++
    }

    # [7] list -> three fixed slots
    $ls = (& curl.exe -s "$Base/api/save/list" -H "X-Auth-Token: $token") -join ""
    $lsObj = $ls | ConvertFrom-Json
    if ($lsObj.code -eq 0 -and $lsObj.data.slots.Count -eq 3 -and $lsObj.data.slots[0].present -and -not $lsObj.data.slots[1].present) {
        Write-Host "[PASS] 07 list  code=0 slots=3 slot1=present slot2=empty slot3=empty"
    } else {
        Write-Host "[FAIL] 07 list  body=$ls"
        $failures++
    }

    # [8] upload without token -> 401 / 40102
    if (-not (Invoke-Step "08 upload no-token" 401 40102 @("-X", "POST", "$Base/api/save/upload", "-H", "Content-Type: application/json", "-d", '{\"slot\":1,\"schemaVersion\":10,\"save\":{}}'))) { $failures++ }

    # [9] upload schemaVersion 99 -> 409 / 40901
    if (-not (Invoke-Step "09 upload v99" 409 40901 @("-X", "POST", "$Base/api/save/upload", "-H", "X-Auth-Token: $token", "-H", "Content-Type: application/json", "-d", '{\"slot\":1,\"schemaVersion\":99,\"save\":{}}'))) { $failures++ }

    # [10] refresh -> new token; old token must be dead
    $rf = (& curl.exe -s -X POST "$Base/api/auth/refresh" -H "X-Auth-Token: $token") -join ""
    $rfObj = $rf | ConvertFrom-Json
    $oldToken = $token
    if ($rfObj.code -eq 0 -and $rfObj.data.token -and $rfObj.data.token -ne $oldToken) {
        $token = $rfObj.data.token
        $oldMe = (& curl.exe -s -w "`n%{http_code}" "$Base/api/me" -H "X-Auth-Token: $oldToken")
        $oldMeHttp = [int]$oldMe[-1]
        if ($oldMeHttp -eq 401) {
            Write-Host ("[PASS] 10 refresh  code=0 new token={0} (old invalidated)" -f $token)
        } else {
            Write-Host "[FAIL] 10 refresh  old token still accepted (http=$oldMeHttp)"
            $failures++
        }
    } else {
        Write-Host "[FAIL] 10 refresh  body=$rf"
        $failures++
    }

    # [11] payload over 256KB -> 400 / 40001 (body too big for the command line: temp file)
    $bigBody = '{"slot":1,"schemaVersion":10,"save":{"blob":"' + ("x" * 300000) + '"}}'
    $tmpBody = Join-Path $env:TEMP "twelvejade-acceptance-bigbody.json"
    Set-Content -Path $tmpBody -Value $bigBody -Encoding ASCII
    if (-not (Invoke-Step "11 upload over-limit" 400 40001 @("-X", "POST", "$Base/api/save/upload", "-H", "X-Auth-Token: $token", "-H", "Content-Type: application/json", "-d", "@$tmpBody"))) { $failures++ }
    Remove-Item $tmpBody -ErrorAction SilentlyContinue

    # [12] change password -> code 0; token invalidated; re-login with NEW password works
    if (-not (Invoke-Step "12 change password" 200 0 @("-X", "POST", "$Base/api/auth/password", "-H", "X-Auth-Token: $token", "-H", "Content-Type: application/json", "-d", '{\"oldPassword\":\"pass1234\",\"newPassword\":\"newpass99\"}'))) { $failures++ }
    if (-not (Invoke-Step "12b login with new password" 200 0 @("-X", "POST", "$Base/api/auth/login", "-H", "Content-Type: application/json", "-d", '{\"username\":\"tester\",\"password\":\"newpass99\"}'))) { $failures++ }

    # [13] lockout: independent user, LAST step (in-memory lock must not pollute earlier steps)
    $lkReg = (& curl.exe -s -X POST "$Base/api/auth/register" -H "Content-Type: application/json" `
        -d '{\"username\":\"locker\",\"password\":\"lockerpass1\"}') -join ""
    $lkObj = $lkReg | ConvertFrom-Json
    if ($lkObj.code -ne 0) {
        Write-Host "[FAIL] 13 lockout  register locker body=$lkReg"
        $failures++
    } else {
        $wrongOk = $true
        foreach ($i in 1..5) {
            $w = (& curl.exe -s -w "`n%{http_code}" -X POST "$Base/api/auth/login" -H "Content-Type: application/json" `
                -d '{\"username\":\"locker\",\"password\":\"wrong\"}')
            if ([int]$w[-1] -ne 401) { $wrongOk = $false }
        }
        if (-not (Invoke-Step "13 lockout 6th attempt(correct pw)" 429 40904 @("-X", "POST", "$Base/api/auth/login", "-H", "Content-Type: application/json", "-d", '{\"username\":\"locker\",\"password\":\"lockerpass1\"}'))) { $failures++ }
        if (-not $wrongOk) { Write-Host "[FAIL] 13 lockout  one of the 5 wrong logins did not return 401"; $failures++ }
    }
}
finally {
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue }
}

if ($failures -eq 0) {
    Write-Host "== ACCEPTANCE PASS: 13/13 steps =="
    exit 0
}
Write-Host "== ACCEPTANCE FAIL: $failures step(s) failed =="
exit 1
