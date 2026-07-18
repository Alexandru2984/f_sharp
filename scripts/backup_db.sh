#!/usr/bin/env bash
# Consistent SQLite backup (works with WAL + live writers) with rotation.
# Usage: backup_db.sh [db_path] [backup_dir] [keep_count]
set -euo pipefail

DB_PATH="${1:-/home/micu/f_sharp/data/finance.db}"
BACKUP_DIR="${2:-/home/micu/backups/finance}"
KEEP="${3:-14}"

# Backups contain password hashes and financial data: keep them private.
mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"
umask 077
STAMP="$(date +%Y%m%d_%H%M%S)"
OUT="$BACKUP_DIR/finance_$STAMP.db"

# .backup takes a consistent snapshot even mid-transaction.
sqlite3 "$DB_PATH" ".backup '$OUT'"
gzip "$OUT"
chmod 600 "$OUT.gz"

# Drop everything beyond the newest $KEEP archives.
ls -1t "$BACKUP_DIR"/finance_*.db.gz 2>/dev/null | tail -n "+$((KEEP + 1))" | xargs -r rm --

echo "backup written: $OUT.gz ($(ls -1 "$BACKUP_DIR" | wc -l) kept)"
