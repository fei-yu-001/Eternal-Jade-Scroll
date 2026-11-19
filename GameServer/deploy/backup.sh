#!/usr/bin/env bash
set -euo pipefail
umask 077
backup_dir=/var/backups/twelvejade
mkdir -p "$backup_dir"
archive="$backup_dir/twelvejade-$(date -u +%Y%m%dT%H%M%SZ).sql.gz"
docker compose -p twelvejade -f /opt/harmonyos/services/eternal-jade-scroll/compose.mysql.yml exec -T mysql sh -c   'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" exec mysqldump -uroot --single-transaction --no-tablespaces --set-gtid-purged=OFF --databases "$MYSQL_DATABASE"' | gzip > "$archive.partial"
gzip -t "$archive.partial"
mv "$archive.partial" "$archive"
# Retention applies only to this service's own generated database archives.
find "$backup_dir" -maxdepth 1 -type f -name 'twelvejade-*.sql.gz' -mtime +14 -delete
printf 'Database backup written: %s\n' "$archive"
