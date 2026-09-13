#!/usr/bin/env bash
# Applies the SQL Server sample schema. Works against either compose profile — the real
# amd64 server or the arm64 Azure SQL Edge one — and needs no sqlcmd.
#
#   ./seed-sqlserver.sh                     # localhost:1433 with the compose credentials
#   ./seed-sqlserver.sh "<connection string>"
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
default_cs="Server=localhost,1433;User ID=sa;Password=ccr_Dev_Password1;Initial Catalog=master;TrustServerCertificate=True;Connect Timeout=30"

dotnet run --project "$here/seed-sqlserver" -- "${1:-$default_cs}" "$here/seed/sqlserver/01-schema.sql"
