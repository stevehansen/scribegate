---
description: Prime context for adding or modifying a Scribegate API endpoint — layers, conventions, wiring.
argument-hint: <endpoint intent, e.g. "add POST /api/v1/repositories/{owner}/{slug}/archive">
---

You are about to add or modify an API endpoint in Scribegate. Load the full vertical-slice context before touching code.

## Required reads (parallelize)

1. `CLAUDE.md` — layer rule, conventions, existing endpoint list (check for duplicates)
2. `docs/architecture.md` — error-handling philosophy, authorization model
3. `src/Scribegate.Web/Api/` — pick 1–2 existing endpoint files closest to the target domain (e.g. `ShareLinkEndpoints.cs`, `TemplateEndpoints.cs`, `WebhookEndpoints.cs`) as templates
4. `src/Scribegate.Web/Api/AuthorizationHelper.cs` and `UserContext.cs` — how auth/role gating is expressed
5. `src/Scribegate.Web/Api/ApiResults.cs` — structured error helpers
6. `src/Scribegate.Web/Program.cs` — endpoint registration wiring

## Vertical-slice checklist

For a new endpoint, work through (in order):

1. **Core** — add/extend entity in `src/Scribegate.Core/` if needed (zero deps).
2. **Data** — EF config in `src/Scribegate.Data/Configurations/`, then generate a migration:
   ```
   dotnet ef migrations add <Name> --project src/Scribegate.Data --startup-project src/Scribegate.Web
   ```
3. **Web/Api** — endpoint file in `src/Scribegate.Web/Api/`, registered in `Program.cs`. Use `{owner}/{slug}` route prefix. Auth via `UserContext`, role gate via `AuthorizationHelper`. Return structured errors via `ApiResults`.
4. **Audit** — emit an audit event for every mutation (see `AuditService.cs`).
5. **OpenAPI** — verify Swagger shows it; client libraries regenerate from the spec.
6. **CLI** — if user-facing, add a `sg` command in `src/Scribegate.Cli/`.
7. **Frontend** — if UI-visible, add API client method under `src/Scribegate.Web/Client/src/api/` and a component/page.
8. **Docs** — update the endpoint table in `CLAUDE.md` and `docs/api.md`.
9. **Tests** — add coverage if a test project exists (check `Scribegate.slnx`).

## Must-ask-yourself

- Public, authenticated, or role-gated? Private-repo read/write paths are a known sensitive surface — confirm `AuthorizationHelper` covers the path you're adding.
- Rate-limited? Only surgical rate limits (auth, share resolve, webhook test). Don't add more without reason.
- Tier/quota impact? Check `TierService.cs`.

## Task

$ARGUMENTS

After reading, propose the endpoint signature (method, route, auth, request/response shapes) and wait for confirmation before implementing.
