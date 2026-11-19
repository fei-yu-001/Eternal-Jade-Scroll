#!/usr/bin/env python3
"""Verify a newly deployed API using one disposable account, including MySQL restart.
Requires Python requests. Restarts only the twelvejade services on the specified host.
"""
import argparse
import json
import secrets
import subprocess
import time
import requests

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--base-url", required=True)
parser.add_argument("--ssh-host", required=True)
parser.add_argument("--ssh-key", required=True)
parser.add_argument("--report", required=True)
args = parser.parse_args()
ssh = ["ssh", "-i", args.ssh_key, "-o", "BatchMode=yes", "-o", "ConnectTimeout=15", args.ssh_host]
checks = []
username = "verify_" + secrets.token_hex(8)
password = secrets.token_hex(24)
account_created = False


def remote(command, data=None):
    result = subprocess.run(ssh + [command], input=data, text=True, encoding="utf-8",
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=240)
    if result.returncode:
        raise RuntimeError("Remote verification command failed: " + result.stderr[-1200:])
    return result.stdout


def call(method, route, token=None, body=None, status=200, code=0):
    headers = {"X-Auth-Token": token} if token else {}
    r = requests.request(method, args.base_url.rstrip("/") + route, headers=headers,
                         json=body, timeout=25)
    result = r.json()
    assert r.status_code == status, (route, r.status_code)
    assert result["code"] == code, (route, result["code"])
    return result.get("data")


def passed(name):
    checks.append(name)
    print("PASS " + name, flush=True)


try:
    assert call("GET", "/api/health")["status"] == "up"
    passed("HTTPS health with certificate verification")
    call("GET", "/api/me", status=401, code=40102)
    passed("unauthenticated cloud access rejected")
    registered = call("POST", "/api/auth/register", body={"username": username, "password": password})
    account_created = True
    token = registered["token"]
    passed("register and authenticate")
    payload = {"coins": 731, "memo": None, "unicode": "十二玉楼", "large": "x" * 200000}
    call("POST", "/api/save/upload", token, {"slot": 1, "schemaVersion": 11, "save": payload})
    downloaded = call("GET", "/api/save/download?slot=1", token)
    assert downloaded["save"] == payload
    stamp = downloaded["updatedAt"]
    slots = call("GET", "/api/save/list", token)["slots"]
    assert len(slots) == 3 and slots[0]["present"] and slots[0]["updatedAt"] == stamp
    passed("200 KB Unicode/null cloud-save roundtrip and slot listing")
    old = token
    token = call("POST", "/api/auth/login", body={"username": username, "password": password})["token"]
    call("GET", "/api/me", old, status=401, code=40102)
    old = token
    token = call("POST", "/api/auth/refresh", token)["token"]
    call("GET", "/api/me", old, status=401, code=40102)
    passed("login/refresh rotate tokens and reject previous tokens")
    remote("sudo systemctl stop twelvejade-api && sudo docker compose -p twelvejade -f /opt/harmonyos/services/eternal-jade-scroll/compose.mysql.yml restart mysql && sudo docker compose -p twelvejade -f /opt/harmonyos/services/eternal-jade-scroll/compose.mysql.yml up -d --wait --wait-timeout 120 && sudo systemctl start twelvejade-api")
    for attempt in range(60):
        try:
            call("GET", "/api/health")
            break
        except (requests.RequestException, ValueError, AssertionError):
            time.sleep(2)
    else:
        raise RuntimeError("API did not recover after restart")
    assert call("GET", "/api/me", token)["username"] == username
    persisted = call("GET", "/api/save/download?slot=1", token)
    assert persisted["save"] == payload and persisted["updatedAt"] == stamp
    passed("account, token and 200 KB save survive database and API restart")
    call("POST", "/api/auth/logout", token)
    call("GET", "/api/me", token, status=401, code=40102)
    passed("logout invalidates token")
finally:
    if account_created:
        # username is generated locally from a fixed ASCII prefix and random hex only.
        sql = ("START TRANSACTION; DELETE FROM saves WHERE user_id IN "
               "(SELECT id FROM users WHERE username='" + username + "'); "
               "DELETE FROM users WHERE username='" + username + "'; COMMIT;\n")
        command = "sudo docker compose -p twelvejade -f /opt/harmonyos/services/eternal-jade-scroll/compose.mysql.yml exec -T mysql sh -c 'MYSQL_PWD=\"$MYSQL_PASSWORD\" exec mysql -u\"$MYSQL_USER\" \"$MYSQL_DATABASE\"'"
        remote(command, sql)
        passed("disposable account and save removed")
        remote("sudo /usr/local/sbin/twelvejade-backup")
        passed("fresh compressed database backup created")

with open(args.report, "w", encoding="utf-8") as f:
    json.dump({"result": "PASS", "checks": checks, "payload_bytes": len(json.dumps(payload).encode())}, f, indent=2)
print("All live deployment checks passed.")
