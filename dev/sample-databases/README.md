# Sample databases

Test servers for exercising CCReimagined against every provider it supports. The schema is
the same story in each dialect, so you can generate the same table four ways and diff the
results.

## The short version

```bash
# PostgreSQL, MySQL and MariaDB — native on this machine, up in seconds
docker compose up -d

# SQLite — no server at all, just a file
./make-sqlite.sh

# SQL Server — emulated, see the caveat below
docker compose --profile mssql up -d
```

## Connection details

| Engine | Host / file | Port | Database | User | Password |
| --- | --- | --- | --- | --- | --- |
| PostgreSQL | `localhost` | 5432 | `ccrsample` | `ccr` | `ccr_dev_password` |
| MySQL | `localhost` | 3306 | `ccrsample` | `ccr` | `ccr_dev_password` |
| MariaDB | `localhost` | 3307 | `ccrsample` | `ccr` | `ccr_dev_password` |
| SQL Server | `localhost` | 1433 | `ccrsample` | `sa` | `ccr_Dev_Password1` |
| SQLite | `./ccrsample.db` | — | — | — | — |

These are throwaway development credentials for a local container. Do not reuse them.

In CCReimagined: pick the engine, fill the fields, **Connect**, then save it as a profile so
it comes back next launch. Passwords are never written to the profile, so you retype them.

## The ARM caveat, and why SQL Server is behind a profile

This machine is aarch64. PostgreSQL, MySQL and MariaDB all publish native arm64 images and
run at full speed.

SQL Server does not. Microsoft ships `mcr.microsoft.com/mssql/server` for **amd64 only**, so
that service is pinned to `linux/amd64` and runs under qemu emulation. It is kept behind a
compose profile so a plain `docker compose up -d` does not drag it in.

Azure SQL Edge used to be the native-arm64 answer, and is what most Apple-silicon guides
still point at. It is retired as of 30 September 2025, and its arm64 build is retired with
it — the image still pulls but gets no updates or support, and it was always a reduced
engine. Since the point of testing here is that the tool's `sys.objects` / `sys.columns`
queries match *real* SQL Server, the emulated but genuine article is the better choice.

Emulation needs the qemu binfmt handlers registered once per boot:

```bash
docker run --privileged --rm tonistiigi/binfmt --install amd64
```

Expect the SQL Server container to take a minute or two to come up, and the seed step to be
unhurried. Once it is running, the metadata queries this tool makes are small, so browsing
feels fine.

## What the schema is for

It is deliberately not a tidy textbook model. Each table exists to push on something the
generator has to get right:

| Table | What it exercises |
| --- | --- |
| `member` | The ordinary case: generated key, bounded and unbounded text, nullable columns, decimal, date, blob. On SQL Server it also carries a `rowversion`, which must never be written. |
| `claim` | A second generated key, on PostgreSQL via the older `serial` spelling rather than `IDENTITY`. |
| `legacy_lookup` | **No generated key at all.** The tool must guess one, warn, and still generate — the case the original refused with a message box. |
| `member_program` | A composite primary key, so no single column is the key. |
| `awkward_names` | Column names that are C# keywords (`int`, `class`), contain a space (`Unit Cost`), collide with a generated member (`ConnectionString`), start with a digit (`2nd_address`), or merely *contain* a keyword (`INTERVIEWER` — must be left alone). |
| `inventory` | A stored generated/computed column: readable, never writable. |
| `settings` | SQLite only. `WITHOUT ROWID`, where even an `INTEGER PRIMARY KEY` does not auto-number. |
| `type_zoo` | Every type the provider maps, so the whole mapping table can be eyeballed at once. |
| `active_member` | A view, which should generate with a warning about the mutating methods. |

## Housekeeping

```bash
docker compose ps                    # health
docker compose logs -f postgres      # why something will not start
docker compose down                  # stop, keep data
docker compose down -v               # stop and wipe, so the seed runs again next time
```

The seed scripts only run when a data volume is created. After editing one, `docker compose
down -v` before bringing it back up. `ccrsample.db` is gitignored — rerun `./make-sqlite.sh`
to rebuild it.
