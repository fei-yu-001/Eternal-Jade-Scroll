#!/usr/bin/env bash
# Run on the authorized Ubuntu host: sudo bash install.sh <release-id> <domain> <existing-cert-name>
set -euo pipefail
umask 077
release=${1:?release id required}
domain=${2:?domain required}
certificate=${3:?existing certificate name required}
[[ $release =~ ^[A-Za-z0-9._-]+$ && $domain =~ ^[A-Za-z0-9.-]+$ && $certificate =~ ^[A-Za-z0-9.-]+$ ]] || exit 2
source_dir=$(cd -- "$(dirname -- "$0")" && pwd)
[[ $EUID -eq 0 && -f "$source_dir/game-server.jar" && -f "$source_dir/V1__user_cloud_save.sql" ]] || exit 2
[[ -f "/etc/letsencrypt/live/$certificate/fullchain.pem" ]] || exit 2
id twelvejade >/dev/null 2>&1 || useradd --system --home-dir /var/lib/twelvejade --shell /usr/sbin/nologin twelvejade
install -d -m 755 /opt/harmonyos/services/eternal-jade-scroll /opt/harmonyos/services/eternal-jade-scroll/releases /opt/harmonyos/services/eternal-jade-scroll/schema
install -d -m 700 /etc/twelvejade /var/backups/twelvejade
if [[ ! -f /etc/twelvejade/mysql.env ]]; then
    root_password=$(openssl rand -hex 32)
    app_password=$(openssl rand -hex 32)
    printf 'MYSQL_ROOT_PASSWORD=%s\nMYSQL_DATABASE=twelvejade\nMYSQL_USER=twelvejade\nMYSQL_PASSWORD=%s\n' "$root_password" "$app_password" > /etc/twelvejade/mysql.env
    printf 'SPRING_PROFILES_ACTIVE=mysql\nSERVER_ADDRESS=127.0.0.1\nTWELVEJADE_PORT=18080\nTWELVEJADE_DB_URL=jdbc:mysql://127.0.0.1:3316/twelvejade?useSSL=false&allowPublicKeyRetrieval=true&connectionTimeZone=UTC\nTWELVEJADE_DB_USERNAME=twelvejade\nTWELVEJADE_DB_PASSWORD=%s\nSPRING_DATASOURCE_HIKARI_MAXIMUM_POOL_SIZE=4\nSPRING_DATASOURCE_HIKARI_MINIMUM_IDLE=1\n' "$app_password" > /etc/twelvejade/api.env
    unset root_password app_password
fi
chmod 600 /etc/twelvejade/*.env
install -m 644 "$source_dir/V1__user_cloud_save.sql" /opt/harmonyos/services/eternal-jade-scroll/schema/01-users-and-saves.sql
install -m 644 "$source_dir/compose.mysql.yml" /opt/harmonyos/services/eternal-jade-scroll/compose.mysql.yml
docker compose -p twelvejade -f /opt/harmonyos/services/eternal-jade-scroll/compose.mysql.yml up -d --wait --wait-timeout 240
install -m 644 "$source_dir/game-server.jar" "/opt/harmonyos/services/eternal-jade-scroll/releases/$release.jar"
sha256sum "/opt/harmonyos/services/eternal-jade-scroll/releases/$release.jar" > "/opt/harmonyos/services/eternal-jade-scroll/releases/$release.sha256"
previous=$(readlink /opt/harmonyos/services/eternal-jade-scroll/current.jar || true)
if ! curl -fsS --max-time 3 http://127.0.0.1:18080/api/health >/dev/null 2>&1; then previous=""; fi
if [[ -n $previous ]]; then printf '%s\n' "$previous" > /opt/harmonyos/services/eternal-jade-scroll/previous-release.txt; fi
ln -sfn "/opt/harmonyos/services/eternal-jade-scroll/releases/$release.jar" /opt/harmonyos/services/eternal-jade-scroll/current.jar
install -m 644 "$source_dir/api.service" /etc/systemd/system/twelvejade-api.service
install -m 755 "$source_dir/backup.sh" /usr/local/sbin/twelvejade-backup
install -m 644 "$source_dir/backup.service" /etc/systemd/system/twelvejade-backup.service
install -m 644 "$source_dir/backup.timer" /etc/systemd/system/twelvejade-backup.timer
systemctl daemon-reload
systemctl enable twelvejade-api.service twelvejade-backup.timer >/dev/null
systemctl restart twelvejade-api.service
healthy=false
for attempt in $(seq 1 60); do
    if curl -fsS --max-time 3 http://127.0.0.1:18080/api/health >/dev/null 2>&1; then healthy=true; break; fi
    sleep 2
done
if [[ $healthy != true ]]; then
    if [[ -n $previous && -f $previous ]]; then
        ln -sfn "$previous" /opt/harmonyos/services/eternal-jade-scroll/current.jar
        systemctl restart twelvejade-api.service
    else
        systemctl stop twelvejade-api.service
    fi
    echo 'API health check failed; previous application restored when available.' >&2
    exit 1
fi
config=/etc/nginx/sites-available/twelvejade
if [[ -f $config ]]; then cp "$config" "$config.before-$release"; fi
sed -e "s/__DOMAIN__/$domain/g" -e "s/__CERT__/$certificate/g" "$source_dir/nginx.conf.template" > "$config"
ln -sfn "$config" /etc/nginx/sites-enabled/twelvejade
if ! nginx -t; then
    if [[ -f $config.before-$release ]]; then cp "$config.before-$release" "$config"; else unlink /etc/nginx/sites-enabled/twelvejade; fi
    exit 1
fi
systemctl reload nginx
systemctl start twelvejade-backup.timer
/usr/local/sbin/twelvejade-backup
curl -fsS --max-time 10 "https://$domain/api/health"
echo
printf 'Deployed release %s at https://%s\n' "$release" "$domain"
