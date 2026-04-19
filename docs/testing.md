# Testing

Scribegate ships three layered test projects plus a Vitest suite for the SPA.
All of it runs on every pull request via `.github/workflows/ci.yml`.

## Layers & where each test belongs

| Layer | Project | Reference | Use for |
|---|---|---|---|
| Core domain | `tests/Scribegate.Core.Tests` | Core only | Pure value/invariant tests, slug/frontmatter helpers, anything that should never touch a DB or HTTP. xUnit v3 + FluentAssertions + NSubstitute. |
| Data + storage | `tests/Scribegate.Data.Tests` | Core + Data | EF configurations, migrations, FTS5 triggers, store-level queries. Spins up a real SQLite *file* per test. |
| Full stack | `tests/Scribegate.Web.Tests` | Core + Data + Web | API endpoints via `WebApplicationFactory<Program>`, auth flows, Markdown parity snapshots. |
| SPA | `src/Scribegate.Web/Client/src/**` | — | Vitest + jsdom. Colocated `.test.ts` files for component logic, shared `src/__tests__/` for cross-cutting/parity. |

Pick the lowest layer that can reach the code under test. A slug-regex test
does not need `WebApplicationFactory`.

## How to add a .NET test

### Core

1. Add a class under `tests/Scribegate.Core.Tests/`.
2. `[Fact]` or `[Theory]` methods with FluentAssertions.
3. Nothing to wire up — the project already references Core.

### Data

1. Add a class under `tests/Scribegate.Data.Tests/`.
2. Instantiate `TempSqliteFixture` inside the test (or promote it to an
   `IAsyncLifetime` fixture if multiple tests share the same DB).
3. Call `CreateAndMigrateAsync()` to get a `ScribegateDbContext` with all
   migrations applied against a unique temp `.db` file.
4. `await using` the fixture — disposal clears the SQLite connection pool
   before `rmdir` so Windows doesn't fight the file locks.

### Web / integration

1. Add a class under `tests/Scribegate.Web.Tests/`.
2. Implement `IClassFixture<ScribegateWebAppFactory>` (or instantiate
   `await using var factory = new ScribegateWebAppFactory()` if each test
   needs a fresh DB).
3. Use `factory.CreateClient()` and speak HTTP — this is the real host
   with real middleware. The factory points `Scribegate:DataPath` at a
   unique temp dir and swaps `IWebhookDispatcher` with a no-op so nothing
   calls out during the test.

## How to add a SPA test

- Colocated unit: put `foo.test.ts` next to `foo.ts`. Vitest picks it up via
  `include: ['src/**/*.test.ts']` in `vite.config.ts`.
- Cross-cutting: put it under `src/__tests__/`.
- The shared setup file (`src/__tests__/setup.ts`) installs an in-memory
  `localStorage` shim so auth state tests are deterministic.
- Use `@open-wc/testing`'s `fixture` / `html` helpers for Lit components.

## Markdown parity workflow

- Corpus: `tests/fixtures/markdown/corpus.json`. Each entry has `id`,
  `description`, and `markdown`.
- Goldens:
  - Markdig: `tests/fixtures/markdown/markdig-golden/{id}.html`
  - marked:  `tests/fixtures/markdown/marked-golden/{id}.html`
- The first time a theory case runs with no golden, the test writes the
  current output and passes (seeding). Subsequent runs assert byte
  equality. **Goldens must be committed** — CI never seeds.
- Intentional pipeline change? Delete the affected golden files and rerun;
  the test will re-seed. Review the diff in `git status` before committing.
- Cross-pipeline (Markdig vs marked) divergence is tracked as a TODO test
  in both sides. The authoritative list of known divergences lives in
  `docs/markdown.md`.

## Flake quarantine

- **.NET:** tag the test with `[Trait("Flaky", "true")]` and include a link
  to the tracking issue in the xml doc. Skip with `[Fact(Skip = "flaky — see #NNN, expires YYYY-MM-DD")]`.
- **Vitest:** use `it.skipIf(process.env.CI)` or `describe.skip` with the
  same comment format.
- Every quarantine entry must carry an issue link and a 30-day expiry. If
  the expiry passes with no fix, delete the test rather than extend the
  skip.

## CI shape

Three parallel jobs (see `.github/workflows/ci.yml`):

- **`test-dotnet`** — matrix `ubuntu-latest` + `windows-latest`. Restores,
  builds, then runs the three xUnit v3 executable test projects via
  `dotnet run --no-build -c Release --project ...`. Coverage collection is
  deferred until the Microsoft.Testing.Platform coverage extension is wired in.
- **`test-frontend`** — ubuntu only. `npm ci`, `tsc --noEmit`, then
  `npm run test:run` followed by `npm run test:parity`.
- **`publish-check`** — unchanged end-to-end build / publish sanity.

## Known gotchas

- **SQLite file locks on Windows.** `Microsoft.Data.Sqlite` pools
  connections. Always call `SqliteConnection.ClearAllPools()` before
  deleting a temp data dir; `TempSqliteFixture` and
  `ScribegateWebAppFactory` both do this.
- **`public partial class Program { }`** is required in
  `src/Scribegate.Web/Program.cs` so `WebApplicationFactory<Program>` can
  find the entry point. Top-level statements generate an internal
  `Program` class by default, which the factory cannot see across
  assemblies.
- **FTS5 needs a real SQLite file.** The `DocumentFts` virtual table and
  its triggers are created via raw SQL in the migration. They work
  against real connections; trying to use `:memory:` or the EF InMemory
  provider will silently drop the FTS table.
- **FTS5 and `content=''`.** The original `DocumentFts` table was
  declared `content='', contentless_delete=1` with `DocumentId
  UNINDEXED`. SQLite's contentless FTS5 tables retain none of their
  column data — even UNINDEXED columns come back NULL, and
  `snippet()` / `highlight()` cannot reconstruct output. The
  `FixFtsRowidJoin` migration rebuilt the table as a plain
  `fts5(Content)` and switched the triggers + search query to link
  via `Documents.rowid`. If you introduce a new FTS virtual table,
  avoid contentless mode unless you genuinely want the index-only
  tradeoff.
- **`Scribegate:DataPath` is read at CreateBuilder time.** Program.cs
  reads the key immediately after `WebApplication.CreateBuilder(args)`
  — *before* any `WebApplicationFactory` `ConfigureAppConfiguration`
  hook fires. Setting the key via the factory's configuration hook
  alone is not enough: the `DbContext` is already registered with the
  default `data/` relative path by the time the hook runs, and every
  test ends up sharing one SQLite file in the test project's
  `bin/Debug/.../data/` directory. `ScribegateWebAppFactory`
  therefore also re-registers the `DbContext` in
  `ConfigureTestServices` with the per-factory temp path. Keep both
  overrides — the configuration hook still gives the app the right
  DataPath for the git mirror root and any future code paths that
  read the key later.
- **Vitest + Node 22+ localStorage.** Modern Node ships a file-backed
  global `localStorage` that warns and has no `.clear()` on worker
  processes. `src/__tests__/setup.ts` replaces it with an in-memory
  shim for every test file.
