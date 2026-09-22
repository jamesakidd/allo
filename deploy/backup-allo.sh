#!/bin/bash
#
# Allo backup.
#
# Runs on the Unraid host via the User Scripts plugin:
#   - scheduled daily, and
#   - run manually BEFORE every container update (migrations don't roll back;
#     container tags do — this archive is the rollback for the schema).
#
# Backs up the database AND the data protection keys together. The keys are not
# incidental: restoring a database without them leaves every family member logged
# out with no way to decrypt their existing cookie.
#
# Why it stops the container instead of copying the file live:
# SQLite is a file, not a server. A copy taken mid-write can be torn, and the
# tear is silent — you find out at restore time. Stopping is a few seconds of
# downtime for a shopping list, and it needs no sqlite3 on the host and no extra
# tooling in the image. The trap restarts the container even if the copy fails.
#
# Restore: stop the container, replace the contents of the appdata folder with
# the contents of an archive, start it again.
#   tar xzf allo_2026-09-22_040000.tar.gz -C /mnt/user/appdata/allo

set -o pipefail

CONTAINER="Allo"                         # `docker ps` name of the Allo container
APPDATA="/mnt/user/appdata/allo"         # the /appdata volume, host side
DEST="/mnt/user/backups/allo"            # backup share/folder on the array
KEEP_DAYS=30                             # prune archives older than this

if [ ! -f "$APPDATA/allo.db" ]; then
    echo "BACKUP FAILED: no database at $APPDATA/allo.db" >&2
    exit 1
fi

mkdir -p "$DEST"
STAMP=$(date +%F_%H%M%S)
FILE="$DEST/allo_${STAMP}.tar.gz"

# Restart the container whatever happens below, including an interrupted run.
WAS_RUNNING=$(docker inspect -f '{{.State.Running}}' "$CONTAINER" 2>/dev/null)
restart_container() {
    if [ "$WAS_RUNNING" = "true" ]; then
        docker start "$CONTAINER" >/dev/null 2>&1
    fi
}
trap restart_container EXIT

if [ "$WAS_RUNNING" = "true" ]; then
    docker stop "$CONTAINER" >/dev/null || { echo "BACKUP FAILED: could not stop $CONTAINER" >&2; exit 1; }
fi

cd "$APPDATA" || exit 1

# -wal and -shm exist only in some journal modes; a database copied without its
# WAL is missing its newest commits, so take them when they are there. Naming the
# files explicitly rather than archiving the folder keeps stray files out.
TARGETS=(allo.db)
if [ -d keys ]; then
    TARGETS+=(keys)
else
    echo "WARNING: no keys directory — a restore from this archive will log everyone out." >&2
fi
for extra in allo.db-wal allo.db-shm; do
    [ -f "$extra" ] && TARGETS+=("$extra")
done

tar czf "$FILE" "${TARGETS[@]}"
TAR_STATUS=$?

# A run that failed part-way still leaves a file; require success AND substance.
if [ $TAR_STATUS -ne 0 ] || [ ! -s "$FILE" ] || [ "$(stat -c%s "$FILE")" -lt 1024 ]; then
    echo "BACKUP FAILED: exit=$TAR_STATUS file=$FILE" >&2
    rm -f "$FILE"
    exit 1
fi

# Prune old archives (only ones this script created).
find "$DEST" -name "allo_*.tar.gz" -mtime +"$KEEP_DAYS" -delete

echo "Backup OK: $FILE ($(du -h "$FILE" | cut -f1))"
