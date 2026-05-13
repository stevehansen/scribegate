# Testing

Scribegate ships three layered .NET test projects, a Vitest suite for the SPA,
and a Playwright smoke suite that drives the full stack from a real browser.
All of it runs on every pull request via `.github/workflows/ci.yml`.

## Layers & where each test belongs

| Layer | Project | Reference | Use for |
|---|---|---|---|
| Core domain | `tests/Scribegate.Core.Tests` | Core only | Pure value/invariant tests, slug/frontmatter helpers, anything that should never touch a DB or HTTP. xUnit v3 + FluentAssertions + NSubstitute. |
| Data + storage | `tests/Scribegate.Data.Tests` | Core + Data | EF configurations, migrations, FTS5 triggers, store-level queries. Spins up a real SQLite *file* per test. |
| Full stack | `tests/Scribegate.Web.Tests` | Core + Data + Web | API endpoints via `WebApplicationFactory<Program>`, auth flows, Markdown parity snapshots. |
| SPA | `src/Scribegate.Web/Client/src/**` | — | Vitest + jsdom. Colocated `.test.ts` files for component logic, shared `src/__tests__/` for cross-cutting/parity. |
| End-to-end | `tests/Scribegate.E2E` | — | Playwright spec(s) that drive the SPA against a real ASP.NET host with a fresh SQLite DB per run. One golden-path smoke spec — auth variants and feature-by-feature coverage stay in the API/SPA layers. |

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

## How to add a Playwright spec

The E2E suite is intentionally tiny — one golden-path smoke spec that proves
the auth → write → review → merge loop works end-to-end. Don't add specs that
duplicate API or component coverage; reach for the layer that exercises the
narrowest surface.

1. Add `specs/<feature>.spec.ts` under `tests/Scribegate.E2E/`. Each spec
   mints its own user(s) via the registration UI or `request.post('/api/v1/auth/register')`
   so the suite is parallel-safe against the single shared SQLite DB the
   webServer launches.
2. Prefer semantic locators (`getByRole`, `getByLabel`, `getByPlaceholder`,
   `getByText`) — they pierce Lit's shadow DOM. CSS selectors do not.
3. If a flow needs a stable hook the accessibility tree can't reach, add a
   `data-testid="..."` to the component and use `getByTestId(...)`. Don't add
   testids speculatively.
4. Self-approval is forbidden by the proposal service. The golden-path spec
   registers a second user via the API and adds them as a Reviewer to avoid
   driving the members-page UI; mirror that pattern when you need a second
   identity.

### Running locally

```bash
cd tests/Scribegate.E2E
npm ci
npm run install:browsers        # one-time, installs Chromium under ~/.cache
npm test                        # full suite, headless
npm run test:headed             # watch a browser
npm run test:debug              # Playwright Inspector
SKIP_CLIENT_BUILD=1 npm test    # reuse the existing wwwroot for fast re-runs
PLAYWRIGHT_PORT=5199 npm test   # change the host port (default 5099)
```

The webServer config builds the SPA, copies `dist/` into
`src/Scribegate.Web/wwwroot/`, then launches `dotnet run --no-launch-profile`
against a unique temp `Scribegate:DataPath` (cleaned up on exit). Playwright
probes `GET /healthz` before running specs.

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

Four parallel jobs (see `.github/workflows/ci.yml`):

- **`test-dotnet`** — matrix `ubuntu-latest` + `windows-latest`. Restores,
  builds, installs the `dotnet-coverage` global tool, then runs each of the
  three xUnit v3 executable test projects via
  `dotnet-coverage collect -f cobertura -o coverage/<layer>.cobertura.xml ...`.
  The `dotnet-coverage` wrapper instruments the process out-of-band and emits
  Cobertura XML — this decouples coverage from the in-runner MTP extension
  whose package versions don't yet line up with `xunit.v3` 2.0.0.
- **`test-frontend`** — ubuntu only. `npm ci`, `tsc --noEmit`,
  `npm run test:coverage` (Vitest with the `cobertura` reporter; output at
  `src/Scribegate.Web/Client/coverage/cobertura-coverage.xml`), then
  `npm run test:parity` as a sanity check.
- **`test-e2e`** — ubuntu only. Builds the SPA + copies it into `wwwroot`,
  builds the ASP.NET host, installs Chromium via a cached browser store,
  then runs the Playwright suite. The Playwright `webServer` config launches
  `dotnet run` against a fresh temp `Scribegate:DataPath` and waits for
  `/healthz` before the first spec.
- **`publish-check`** — unchanged end-to-end build / publish sanity.

## Coverage artifacts

Each CI run uploads three workflow artifacts:

| Artifact | Job | Contents |
|---|---|---|
| `coverage-dotnet-ubuntu-latest` | test-dotnet (linux) | `core.cobertura.xml`, `data.cobertura.xml`, `web.cobertura.xml` |
| `coverage-dotnet-windows-latest` | test-dotnet (windows) | same three files |
| `coverage-frontend` | test-frontend | `cobertura-coverage.xml` (Vitest v8 provider) |

Open the run in GitHub Actions and download the artifact zip to inspect a
report locally (every Cobertura viewer — VS Code "Coverage Gutters",
ReportGenerator, IntelliJ — accepts these files unchanged).

### Coverage gate

The `test-dotnet` and `test-frontend` jobs each run `scripts/check-coverage.mjs`
against their Cobertura output and **fail the build if any layer's measured
line-rate drops below the floor** in `coverage-thresholds.json`. It's a
soft floor (regression detector), not a hard target — each entry is the first
measured value rounded down by ~2 percentage points, and the values move up
only after the suite stabilises at a new level. Tightening a floor when you
add tests is a one-line PR.

A separate `coverage-badge` job runs only on `main` pushes: it downloads the
ubuntu Cobertura artifacts, weights each layer by its `lines-valid` count,
and force-pushes a `coverage.json` in Shields.io endpoint format to the
`coverage-data` orphan branch. The README badge reads that file via
`raw.githubusercontent.com` — PR branches never touch it.

### Generating coverage locally

```bash
# .NET — one file per test project
dotnet tool install --global dotnet-coverage
dotnet build Scribegate.slnx -c Release -p:SkipClientBuild=true
dotnet-coverage collect -f cobertura -o coverage/core.cobertura.xml \
  "dotnet run --no-build -c Release --project tests/Scribegate.Core.Tests"

# Frontend
cd src/Scribegate.Web/Client && npm run test:coverage
# → src/Scribegate.Web/Client/coverage/cobertura-coverage.xml
```

The `coverage/` directories are gitignored.

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
