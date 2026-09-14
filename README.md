# CCReimagined

[![build](https://github.com/Harlock123/CCReimagined/actions/workflows/build.yml/badge.svg)](https://github.com/Harlock123/CCReimagined/actions/workflows/build.yml)

A cross-platform rebuild of [CodeComplete](https://github.com/Harlock123/codecomplete) — the
WinForms tool that pointed at a SQL Server table and wrote a full data-abstraction class for it.

Two things change here. It runs natively on macOS, Windows and Linux via Avalonia, and it is no
longer tied to one database engine: discovery and code generation both go through provider
interfaces, so the tool can browse PostgreSQL and the class it writes can use Npgsql.

This first cut focuses on the database orchestration — connect, browse, read schema, generate the
data class. The original tool's other generators (XAML, WinForms, HTML/CSS, TypeScript, the LM
Studio integration, the subnet scanner) are deliberately not carried over yet.

## Engines

| Engine | Browsing | Generated code uses |
| --- | --- | --- |
| Microsoft SQL Server | `sys.objects`, `sys.columns` | `Microsoft.Data.SqlClient` |
| PostgreSQL | `information_schema`, `pg_database` | `Npgsql` |
| MySQL / MariaDB | `information_schema` | `MySqlConnector` |
| SQLite | `sqlite_master`, `PRAGMA table_xinfo` | `Microsoft.Data.Sqlite` |

The engine you browse and the engine the generated class targets are separate choices. Browse a
SQL Server database, pick "PostgreSQL (Npgsql)" on the **Generate** tab, and you get a class that
talks to Postgres from the SQL Server schema — which is what porting a table between engines needs.

## Layout

```
src/CCReimagined.Core      provider-neutral model, schema discovery, code generation
  Model/                   ColumnInfo, TableSchema, ClrTypeKind, ConnectionSettings
  Providers/               IDatabaseProvider + one implementation per engine
  Codegen/                 ICodegenProfile + one profile per engine, SqlBuilder, the generator
src/CCReimagined.App       Avalonia UI (MVVM, CommunityToolkit.Mvvm, SyntaxColorizer)
tests/CCReimagined.Core.Tests
```

The two seams that matter:

- **`IDatabaseProvider`** — everything needed to explore an engine at design time: build a
  connection string, list databases, list tables and views, read a table's columns. Each provider
  maps its native type names onto the neutral `ClrTypeKind` once, during discovery.
- **`ICodegenProfile`** — everything that differs in the *emitted* code: the ADO.NET type names,
  the parameter sigil, the DbType enum, identifier quoting, and how a generated key comes back
  after an INSERT. The generator is written once against this interface.

Adding an engine — Oracle being the obvious next one — means writing one of each. No change to the
generator, and no change to the UI.

## What it generates

Given a table and the choices you make in the grid, you get one `partial` class with:

- A property per column, typed from the database (`int`, `string?`, `DateOnly?`, `byte[]`…), with
  optional truncating setters for length-bounded text.
- `Initialize()` and `CopyFields(DbDataReader)`.
- `ReadAsync` / `AddAsync` / `UpdateAsync` / `DeleteAsync` / `RecExistsAsync` / `ReadAsDataTableAsync`,
  each with an optional blocking wrapper.
- `GetAllAsync`, plus a `GetListBy<Column>Async` for every column you tick as a **list param** —
  the modern form of the original tool's "what field to use for the list getters" prompt.

The SQL lives in `const` raw string literals at the top of the class, so it is reviewable rather
than assembled at runtime.

### Differences from the original generated classes

The shape is deliberately familiar, but some behaviours were fixed rather than carried forward:

- **Nullable columns become nullable properties.** The old classes used sentinels (`""`, `DateTime`
  min), so "not set" and "legitimately empty" were indistinguishable. Binding an empty string as
  NULL is still available as an opt-in on the **Generate** tab.
- **Reserved-word handling is an exact match.** The original tested whether a column name *contained*
  a keyword, so `INTERVIEWER` was renamed because it contains `int`.
- **Columns are ordered as the table declares them**, not alphabetically.
- **Identity detection covers more than `IDENTITY`** — serial defaults, `AUTO_INCREMENT`, and
  SQLite's `INTEGER PRIMARY KEY` rowid alias, which only auto-numbers on a rowid table.
- **A table with no auto-numbering key still generates.** The old tool showed a modal warning and
  assumed the first column; here the assumption is shown in the summary line and the key is a
  checkbox you can move.
- **`ReadAsDataSet` became `ReadAsDataTableAsync`**, built on `DataTable.Load` over a reader, because
  not every provider ships a `DataAdapter`.
- **Parameters carry their declared size** for bounded text and binary columns, so the server reuses
  one cached plan instead of one per value length.
- **A read-only view generates a read-only class.** The original emitted `Add`, `Update` and
  `Delete` for any view and left you to find out at runtime.

### Views

A class generated from a view only gets `Add`, `Update` and `Delete` when the database allows
writes through it. The engines differ in how they answer, and one of them answers wrongly, so
each is asked in the way that is actually reliable:

| Engine | How it is determined |
| --- | --- |
| PostgreSQL | `information_schema.views.is_updatable`, plus `is_trigger_updatable` for an INSTEAD OF trigger |
| MySQL / MariaDB | `information_schema.views.is_updatable` |
| SQLite | Read-only unless an INSTEAD OF trigger exists — SQLite refuses writes through a view outright |
| SQL Server | `IS_UPDATABLE` exists but reports `NO` for views that accept an `UPDATE`, so it is not read. An INSTEAD OF trigger proves updatability; otherwise the answer is *unknown* |

Unknown is treated generously: the mutating methods are still generated, and the uncertainty is
recorded in the warnings rather than capability being removed silently. The **Generate** tab has a
three-state tick to overrule the database in either direction.

## Saved connections

The **Profile** row at the top of the window saves the connection fields under a name. Type a new
name in *Save as* and press **Save** to create one; keep the name to overwrite. The profile you
last connected with is restored on the next launch, along with the editor theme.

Profiles live in a JSON file under the platform config directory — `~/.config/CCReimagined` on
Linux, `~/Library/Application Support/CCReimagined` on macOS, `%APPDATA%\CCReimagined` on Windows.
The path is shown on the **Connection** tab.

**Passwords are never written to disk.** A profile keeps the host, port, database, user name and
options; you supply the password on connect. This also holds for a saved raw connection string:
every password-bearing keyword (`Password`, `Pwd`, `Client Secret`, and friends) is stripped before
the profile is persisted, so pasting a full connection string cannot leak a secret into the file.
The file is written `0600` on Unix anyway, since it still maps out which servers you reach.

## Running it

```
dotnet run --project src/CCReimagined.App
dotnet test
```

Requires the .NET 10 SDK.

### Builds

Every push to `main` runs the suite and cross-publishes all six targets; a `v*` tag does the
same and attaches them to a GitHub release.

```
./build.sh                              # all six targets, one self-contained file each
./build.sh --targets "win-x64 osx-arm64"
./build.sh --mode aot                   # native, this machine's OS only
```

| | x64 | arm64 |
| --- | --- | --- |
| Linux | `linux-x64` | `linux-arm64` |
| Windows | `win-x64` | `win-arm64` |
| macOS | `osx-x64` | `osx-arm64` |

The default publish is self-contained and single-file — one executable per target with the .NET
runtime and Avalonia's native libraries inside it, so there is nothing to install alongside it.
Ahead-of-time compilation is available with `--mode aot`: faster to start and smaller, but it is
**not** a single file (`libSkiaSharp` and `libHarfBuzzSharp` must stay beside the executable) and
it cannot cross operating systems, so CI treats it as an opt-in extra rather than what ships.

### A note on the Avalonia version

The app is pinned to **Avalonia 11.3** rather than 12, because the generated-code editor uses
[SyntaxColorizer](https://github.com/Harlock123/SyntaxColorizer), whose published package (1.0.2)
is built against Avalonia 11. The library's source retargets to Avalonia 12 + AvaloniaEdit 12
without a single code change, so moving this app to 12 is a matter of publishing a v12 build of
that package first. `Avalonia.Controls.DataGrid` tops out at 11.3.13 on the 11.x line while the
rest sit at 11.3.22 — that combination is deliberate, since 11.3.22 pulls a patched
`Tmds.DBus.Protocol` and still satisfies the DataGrid's dependency.

### Getting out

The window carries its own menu, because a titlebar close button is not something every desktop
provides — a tiling window manager draws none, and relies on its own keybinding instead.

| | |
| --- | --- |
| **File > Exit** | Windows and Linux, in the window's menu bar |
| **CCReimagined > Quit** | macOS, in the system menu bar |
| `Ctrl+Q` | Windows and Linux |
| `Cmd+Q` | macOS |
| `Ctrl+S` | Save the generated class |

On Linux the app currently runs through XWayland rather than natively on Wayland. It works, but it
is worth revisiting if Avalonia's Wayland backend matures.

## Tests

Tests never touch your real config directory — every view model in the suite is handed a profile
store under a temp path.

The suite creates a real SQLite database, discovers it through the provider, generates a class,
**compiles that class with Roslyn**, and drives CRUD through it by reflection — so a generation
change that produces code which does not build, or does not round-trip a row, fails the build.
The view-model tests run the same connect → browse → select → generate sequence the window does.

### Live-server tests

Each engine emits a different class — a different ADO.NET client, a different DbType enum, and
above all a different way of handing back a generated key (PostgreSQL returns it from the INSERT,
MySQL fetches it with `LAST_INSERT_ID`). `LiveCrudTests` compiles the generated class and runs
insert, key read-back, read, update, exists, list getters and delete against a real server, for
PostgreSQL, MySQL, MariaDB and SQL Server.

They need the sample servers from `dev/sample-databases`. When a server is not reachable the
tests **skip** rather than fail, so a bare checkout with nothing running still goes green:

```
docker compose -f dev/sample-databases/docker-compose.yml up -d
dotnet test
```

Point them elsewhere with a full connection string per engine:

```
CCR_TEST_POSTGRES=... CCR_TEST_MYSQL=... CCR_TEST_MARIADB=... CCR_TEST_SQLSERVER=... dotnet test
```

The SQL Server target creates its test database if it is missing, so it needs a reachable server
and nothing else. On arm64 that means the `mssql-arm` profile — see
`dev/sample-databases/README.md` for why the real amd64 image will not run there.
