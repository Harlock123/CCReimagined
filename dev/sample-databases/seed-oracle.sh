#!/usr/bin/env bash
# Applies the Oracle sample schema. The gvenzl/oracle-free image ships sqlplus, so
# unlike SQL Server this needs no helper program.
#
#   ./seed-oracle.sh                  the ccr-oracle container from docker-compose
#   ./seed-oracle.sh <container>
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
container="${1:-ccr-oracle}"

if ! docker ps --format '{{.Names}}' | grep -qx "$container"; then
    echo "Container '$container' is not running. Start it with:" >&2
    echo "  docker compose --profile oracle up -d" >&2
    exit 1
fi

docker cp "$here/seed/oracle/01-schema.sql" "$container:/tmp/ccr-oracle-seed.sql"
docker exec -i "$container" bash -lc \
    'sqlplus -s ccr/ccr_dev_password@localhost:1521/FREEPDB1 <<< "
     WHENEVER SQLERROR EXIT SQL.SQLCODE
     @/tmp/ccr-oracle-seed.sql
     EXIT"'

echo "Oracle sample schema applied to $container."
