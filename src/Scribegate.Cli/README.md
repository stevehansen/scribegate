# Scribegate CLI (`sg`)

Command-line tool for the [Scribegate](https://github.com/stevehansen/scribegate) markdown collaboration platform. Wraps the REST API with human-friendly output and a global `--json` flag for scripts and AI agents.

## Install

```bash
dotnet tool install -g Scribegate.Cli
```

Update:

```bash
dotnet tool update -g Scribegate.Cli
```

## Authenticate

The CLI targets `https://scribegate.dev` by default. Point it at a different instance with `sg auth host` or the `--host` option on `login`/`token`.

```bash
sg auth host                                  # show current host
sg auth host https://scribegate.example.com   # validate via /healthz and save
```

With email and password (host optional — saved on success):

```bash
sg auth login me@example.com my-password
sg auth login me@example.com my-password --host https://scribegate.example.com
```

Or set an API token directly (for CI, agents, scripts — tokens start with `sg_`). The token is verified against the server before it is persisted, so a token from `https://scribegate.dev` won't silently land in a config pointed at `localhost`:

```bash
sg auth token sg_xxxxxxxxxxxxxxxx
sg auth token sg_xxxxxxxxxxxxxxxx --host https://scribegate.dev
```

Verify — `status` always prints the host, even when not authenticated:

```bash
sg auth status
```

`SCRIBEGATE_HOST` and `SCRIBEGATE_TOKEN` environment variables override the saved config. Otherwise both are stored in the OS user profile.

## Commands

```bash
sg repo list
sg repo create "Company Handbook" --description "Internal policies" --visibility private
sg repo view company-handbook

sg doc list company-handbook
sg doc view company-handbook hr/vacation.md
sg doc create company-handbook hr/vacation.md --file ./vacation.md --message "Initial policy"
sg doc edit company-handbook hr/vacation.md --content "..." --message "Bump to 25 days"
sg doc history company-handbook hr/vacation.md
sg doc share company-handbook hr/vacation.md --expires 7 --description "Q2 review copy"
sg doc shares company-handbook                        # list links for the whole repo
sg doc shares company-handbook --path hr/vacation.md  # list links for one document
sg doc unshare company-handbook <link-id>             # revoke a share link

sg proposal list company-handbook --status open
sg proposal create company-handbook \
    --title "Increase vacation days to 25" \
    --document hr/vacation.md \
    --file ./vacation-updated.md \
    --description "Per HR directive 2026-04"
sg proposal view company-handbook <id>
sg proposal approve company-handbook <id>
sg proposal reject company-handbook <id>
sg proposal withdraw company-handbook <id>

sg review list company-handbook <proposal-id>
sg review create company-handbook <proposal-id> --verdict approve --body "LGTM"
```

Every command supports `--json` for machine-readable output:

```bash
sg repo list --json | jq '.[].slug'
```

## AI agent workflow

```bash
# Fetch current content
CURRENT=$(sg doc view handbook hr/vacation.md)

# Agent produces updated content, then opens a proposal from stdin
printf '%s' "$UPDATED" | sg proposal create handbook \
    --title "Update vacation days to 25" \
    --document hr/vacation.md \
    --file - \
    --description "Per HR directive 2026-04" \
    --json
```

Humans stay in the approval loop — agents can propose and comment, but approval requires a human reviewer (by default).

## Links

- [Source & issues](https://github.com/stevehansen/scribegate)
- [Documentation](https://docs.scribegate.dev)
- [Self-hosting guide](https://github.com/stevehansen/scribegate/blob/main/docs/self-hosting.md)

## License

[FSL-1.1-MIT](https://github.com/stevehansen/scribegate/blob/main/LICENSE.md) — free to use, modify, and self-host; converts to MIT 2 years after each release.
