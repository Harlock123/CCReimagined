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

# SQL Server — pick one of these, see the caveat below
docker compose --profile mssql up -d        # real SQL Server, amd64 hosts
docker compose --profile mssql-arm up -d    # Azure SQL Edge, arm64 hosts

# Oracle — native arm64, but a heavy image and a slow first start
docker compose --profile oracle up -d
./seed-oracle.sh
```

Oracle's **Database** field is a *service name*, not a schema — `FREEPDB1` on the Free edition.
It is the only engine here where those differ, and getting it wrong produces ORA-50201, whose
message blames the syntax of the connect string rather than the name in it:

```
DATA SOURCE=localhost:1521/FREEPDB1   connects
DATA SOURCE=localhost:1521/CCR        ORA-50201 — CCR is the schema, not the service
```

Once connected, the left rail lists **Schemas** rather than Databases. Selecting one filters the
relation list; it does not reconnect, because a schema is part of an object's name and not
somewhere to connect to.

## Connection details

Every value below was verified against the running containers. All five carry the same sample
schema — 7 tables and a view, plus a `settings` table only SQLite has.

### In the app

Pick the engine, fill these in, press **Connect**, then **Save** with a profile name so it comes
back next launch. The password is never saved; you retype that one field.

| | PostgreSQL | MySQL | MariaDB | SQL Server | SQLite |
| --- | --- | --- | --- | --- | --- |
| Engine | PostgreSQL | MySQL / MariaDB | MySQL / MariaDB | Microsoft SQL Server | SQLite |
| Server | `localhost` | `localhost` | `localhost` | `localhost` | — |
| Port | `5432` | `3306` | `3307` | `1433` | — |
| Database | `ccrsample` | `ccrsample` | `ccrsample` | `ccrsample` | `./ccrsample.db` |
| Integrated security | **untick** | n/a | n/a | **untick** | n/a |
| user | `ccr` | `ccr` | `ccr` | `sa` | — |
| password | `ccr_dev_password` | `ccr_dev_password` | `ccr_dev_password` | `ccr_Dev_Password1` | — |
| Trust cert | either | either | either | **tick** | n/a |

Two things catch people out:

- **Untick "Integrated security" for PostgreSQL.** Postgres advertises integrated auth because
  peer/ident over a Unix socket is its equivalent, so the box starts ticked — and while it is,
  the user and password fields stay disabled and no password reaches the connection string. The
  failure looks like a server problem rather than a missing password. MySQL and MariaDB do not
  offer it, so the fields are always live there.
- **SQL Server's password is different**, and deliberately so: `ccr_Dev_Password1` satisfies the
  complexity rules the engine enforces at startup. Note the capital D and the trailing digit.

### As raw connection strings

Tick **Raw connection string** and paste one of these, or use them from any other client. These
are exactly what the providers compose from the fields above.

```
PostgreSQL   Host=localhost;Port=5432;Database=ccrsample;Username=ccr;Password=ccr_dev_password;SSL Mode=Prefer

MySQL        Server=localhost;Port=3306;User ID=ccr;Password=ccr_dev_password;Database=ccrsample;SSL Mode=Preferred

MariaDB      Server=localhost;Port=3307;User ID=ccr;Password=ccr_dev_password;Database=ccrsample;SSL Mode=Preferred

SQL Server   Server=localhost,1433;Initial Catalog=ccrsample;User ID=sa;Password=ccr_Dev_Password1;TrustServerCertificate=True;Encrypt=False

SQLite       Data Source=/absolute/path/to/ccrsample.db;Mode=ReadWrite
```

A saved profile keeps a raw connection string too, with every password-bearing keyword stripped
out first — so the stored copy is missing its password and needs it retyped, by design.

### From a shell

```bash
docker exec -it ccr-postgres psql -U ccr -d ccrsample
docker exec -it ccr-mysql    mysql   -uccr -pccr_dev_password ccrsample
docker exec -it ccr-mariadb  mariadb -uccr -pccr_dev_password ccrsample
sqlite3 ./ccrsample.db
```

Azure SQL Edge ships no client tools, so there is no `docker exec` equivalent for SQL Server —
use the app, or any client pointed at `localhost:1433`.

### For the live tests

The test suite finds these on its own and skips when one is not up. Override any of them:

```bash
CCR_TEST_POSTGRES=...  CCR_TEST_MYSQL=...  CCR_TEST_MARIADB=...  CCR_TEST_SQLSERVER=...
```

These are throwaway development credentials for local containers. Do not reuse them anywhere.

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

**On this machine that is not enough.** With the handlers installed, `sqlservr` still segfaults
immediately (exit 139) under qemu-user. Docker Desktop on Apple silicon gets away with running
the same image because Rosetta 2 is a far more complete x86 translator than qemu-user is. So on
arm64 Linux the amd64 image is, in practice, a non-starter.

The workable arm64 target is **Azure SQL Edge**, which has a native arm64 build:

```bash
docker compose --profile mssql-arm up -d
```

It is the same T-SQL engine family — it reports as SQL Server 15.0, the `sys.*` catalog views
this tool queries all behave, and the generated `Microsoft.Data.SqlClient` code round-trips
against it. It is also retired, frozen and a reduced engine, so treat it as a test target and
not as a model of production. If you need certainty about real SQL Server behaviour, run the
amd64 service on an amd64 host.

Both services bind port 1433, so run one profile or the other, never both.

Either way the sample schema has to be applied by hand, because SQL Server has no
`docker-entrypoint-initdb.d` convention:

```bash
./seed-sqlserver.sh
```

That script needs no `sqlcmd` — it uses the same .NET client the generated code does, which is
what makes it work on arm64, where the `mssql-tools` image does not exist and Edge ships no
client tools of its own.

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
