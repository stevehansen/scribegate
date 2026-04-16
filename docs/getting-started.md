# Getting Started

This guide walks you through setting up Scribegate, creating your first repository, and going through the full propose-review-approve workflow.

## 1. Start the Server

=== "Docker"

    ```bash
    docker run -d \
      -p 8080:8080 \
      -v scribegate-data:/data \
      ghcr.io/scribegate/scribegate:latest
    ```

=== "From Source"

    ```bash
    git clone https://github.com/stevehansen/scribegate.git
    cd scribegate
    dotnet run --project src/Scribegate.Web
    ```

Open `http://localhost:8080`. You should see the login page.

## 2. Register the First User

The first user to register automatically becomes the instance admin.

```bash
curl -X POST http://localhost:8080/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "username": "jane",
    "email": "jane@example.com",
    "password": "a-secure-password",
    "acceptTos": true
  }'
```

Save the `token` from the response — you'll need it for subsequent requests.

```bash
export TOKEN="eyJhbGciOiJIUzI1NiIs..."
```

## 3. Create a Repository

```bash
curl -X POST http://localhost:8080/api/v1/repositories \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Company Handbook",
    "description": "Internal policies and procedures",
    "visibility": "Private"
  }'
```

## 4. Create a Document

```bash
curl -X POST http://localhost:8080/api/v1/repositories/company-handbook/documents \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "path": "hr/vacation-policy.md",
    "content": "# Vacation Policy\n\nAll employees receive 20 vacation days per year.",
    "message": "Initial vacation policy"
  }'
```

## 5. Propose a Change

Register a second user (the contributor), then create a proposal:

```bash
curl -X POST http://localhost:8080/api/v1/repositories/company-handbook/proposals \
  -H "Authorization: Bearer $CONTRIBUTOR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "documentPath": "hr/vacation-policy.md",
    "title": "Increase vacation days to 25",
    "description": "Per HR directive 2026-04",
    "content": "# Vacation Policy\n\nAll employees receive 25 vacation days per year."
  }'
```

## 6. Review and Approve

As the admin (who has Reviewer role), approve the proposal:

```bash
curl -X POST http://localhost:8080/api/v1/repositories/company-handbook/proposals/{id}/approve \
  -H "Authorization: Bearer $TOKEN"
```

This creates a new immutable revision, signs it with ECDSA P-256, and updates the document.

## Next Steps

- **Add members** — invite contributors and reviewers to your repository
- **Configure approval rules** — set `requiredApprovals` on your repository (1-10)
- **Set up SSO** — configure OIDC via admin settings for single sign-on
- **Enable notifications** — configure SMTP for email notifications
- **Explore the API** — visit `/swagger` for interactive API docs
- **Use the CLI** — install the `sg` global tool for command-line access

See the [Self-Hosting Guide](self-hosting.md) for production deployment options.
