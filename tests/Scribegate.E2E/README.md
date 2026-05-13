# Scribegate end-to-end tests

Playwright smoke suite that drives the SPA against a real ASP.NET host with a
fresh SQLite database per run.

See `docs/testing.md` § Playwright for conventions and how to add new specs.

## Running locally

```bash
npm ci
npm run install:browsers      # one-time; installs Chromium under node_modules
npm test                      # full suite, headless
npm run test:headed           # watch a browser window
npm run test:debug            # Playwright Inspector
```

The Playwright config builds the SPA, copies it into `src/Scribegate.Web/wwwroot`,
and launches `dotnet run` against a unique temp `Scribegate:DataPath`. The host
is killed on test exit and its data directory removed.

To skip the SPA build on iterative runs (e.g. when you've already built it):

```bash
SKIP_CLIENT_BUILD=1 npm test
```

To pin a different port (default 5099):

```bash
PLAYWRIGHT_PORT=5199 npm test
```
