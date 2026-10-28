# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers what the tool generates, the engines,
saved connections and the Avalonia version pin; this file covers how the code fits together and the rules that
keep it working.

## What this is

CCReimagined is a cross-platform rebuild of the WinForms tool CodeComplete. It connects to a database, browses its
tables and views, reads a table's schema and writes a C# data-access class for it. It is C# on .NET 10 with an
Avalonia 11.3 UI. It supports PostgreSQL, MySQL, MariaDB, SQL Server, Oracle and SQLite.

## Commands

```bash
dotnet build CCReimagined.slnx                  # no global.json: any .NET 10 SDK
dotnet test CCReimagined.slnx                   # live-server tests skip when no database answers
dotnet run --project src/CCReimagined.App       # run it
docker compose -f dev/sample-databases/docker-compose.yml up -d    # PostgreSQL, MySQL and MariaDB for the live tests
CCR_DUMP_DIR=/tmp/ccr dotnet test --filter SampleOutputWriter       # write one sample class per engine to look at
./build.sh [--targets "linux-x64 win-x64"] [--version 1.2.0] [--mode aot]   # release archives in dist/
docs/developer-guide/build.sh                   # re-render the developer guide PDF (needs Chromium or Chrome)
```

The live tests take a connection string per engine from `CCR_TEST_POSTGRES`, `CCR_TEST_MYSQL`, `CCR_TEST_MARIADB`,
`CCR_TEST_SQLSERVER` and `CCR_TEST_ORACLE`. Without one, they use the sample servers' defaults. SQL Server and
Oracle need their own compose profiles. `dev/sample-databases/README.md` explains them, including the arm64 caveat
for SQL Server.

## Layout

- `src/CCReimagined.Core/`: everything except the window, with no Avalonia in it.
  - `Model/`: `ColumnInfo`, `TableRef`, the neutral `ClrTypeKind`, `ConnectionSettings` and `FilePath`.
  - `Providers/`: `IDatabaseProvider` and one implementation per engine, for discovery at design time.
    `ProviderRegistry` lists them.
  - `Codegen/`: `ICodegenProfile` and one profile per engine (the emitted ADO.NET types, parameter sigil, quoting
    and key read-back). `CSharpDataClassGenerator` is written once against that interface, with `SqlBuilder`.
  - `Profiles/`: `ProfileStore`, the saved connections.
- `src/CCReimagined.App/`: the Avalonia UI (CommunityToolkit.Mvvm, SyntaxColorizer for the code view).
  - `ViewModels/MainViewModel.cs` drives connect, browse, select and generate.
  - `Services/IShellServices.cs` hides the clipboard, file dialogs and shutdown so the view model can be tested.
- `tests/CCReimagined.Core.Tests/`: xUnit v2 with `Xunit.SkippableFact`.
  - `GeneratedCodeCompiler` compiles generated classes with Roslyn.
  - `LiveCrudTests` and `LiveServers` run them against real servers.
- `dev/sample-databases/`: Docker Compose servers, a schema per dialect, and seed scripts for SQLite, SQL Server and
  Oracle.
- `docs/`: the developer guide, as HTML and CSS with the rendered PDF.

## Rules that keep it working

- **Two seams per engine.** A new engine is one `IDatabaseProvider` and one `ICodegenProfile`. The provider hands
  out its profile through `CodegenProfile` and is listed in `ProviderRegistry`. Keep engine-specific choices out of
  the generator and the UI.
- **Generated code must compile.** The tests build each generated class with Roslyn and drive CRUD through it by
  reflection. Change the generator only with those tests passing.
- **Passwords never reach disk.** `ProfileStore` strips every password-bearing keyword before saving, including
  ones inside a raw connection string. Keep it that way for any new field.
- **Avalonia stays on 11.3** until SyntaxColorizer ships a build for Avalonia 12. `Avalonia.Controls.DataGrid` is
  11.3.13 on purpose, while the rest are 11.3.22.
- **Tests never touch the user's profiles.** Profiles live in `connections.json` under `CCReimagined` in
  `SpecialFolder.ApplicationData`. Every view model in the tests gets a store under a temp path.
- **Line endings.** Most files are LF, but a few in `src/CCReimagined.App` are CRLF. Keep each file's existing
  line endings.

## Making a change

- Add or update tests in `CCReimagined.Core.Tests` with every change. Run the live tests against the sample
  servers when you touch a provider or a codegen profile.
- To see what a generator change did, write the samples with `CCR_DUMP_DIR` before and after and diff them.
- Update `README.md` and the developer guide in `docs/developer-guide/` to match.

## CI and releases

`.github/workflows/build.yml` runs on pushes and pull requests to `main`, on `v*` tags and by hand.

- It runs the suite. A separate job runs it again against PostgreSQL, MySQL, MariaDB, SQL Server and Oracle service
  containers, and fails if any test skipped.
- After the tests pass, `./build.sh` publishes self-contained single files for linux, win and osx, each x64 and
  arm64. They are uploaded as an artifact.
- A `v*` tag attaches them to a GitHub release. AOT native builds run only when asked for by hand.
