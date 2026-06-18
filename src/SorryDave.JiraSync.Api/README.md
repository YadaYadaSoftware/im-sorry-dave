# jira-sync-core API — Operations & Configuration

Deployment and configuration guide for the `SorryDave.JiraSync.Api` service. For an overview
of the platform see the [repository README](../../README.md).

The service mirrors Jira work items (webhooks + reconciliation) and writes decisions/answers
back to Jira via an outbox. Runtime: **.NET 10** (ASP.NET Core).

---

## Configuration model

Settings come from standard ASP.NET Core configuration providers, applied in this order (later
overrides earlier):

1. `appsettings.json` (committed defaults / examples)
2. `appsettings.{Environment}.json`
3. **Environment variables** (preferred for servers/containers)
4. **User-secrets** (local dev only — never deployed)
5. Command-line arguments

> Secrets (`Jira:ApiToken`, `Webhook:Secret`) must come from environment variables or a secret
> store in any shared/production environment. Never commit them to `appsettings.json`.

### Environment-variable naming

ASP.NET Core maps nested keys using a **double underscore** (`__`) separator. So:

| Config key | Environment variable |
|---|---|
| `Jira:BaseUrl` | `Jira__BaseUrl` |
| `Jira:Email` | `Jira__Email` |
| `Jira:ApiToken` | `Jira__ApiToken` |
| `Jira:ProjectKeys:0` | `Jira__ProjectKeys__0` |
| `Webhook:Secret` | `Webhook__Secret` |
| `ConnectionStrings:JiraSync` | `ConnectionStrings__JiraSync` |
| `Sync:ReconciliationInterval` | `Sync__ReconciliationInterval` |

---

## Settings reference

### `Jira` section

| Key | Required | Default | Description |
|---|---|---|---|
| `Jira:BaseUrl` | yes (real mode) | — | Jira Cloud site, e.g. `https://your-org.atlassian.net` |
| `Jira:Email` | yes (real mode) | — | Atlassian account email used with the API token (HTTP Basic) |
| `Jira:ApiToken` | yes (real mode) | — | API token — **secret** |
| `Jira:ProjectKeys` | recommended | `["DAVE"]` | Project keys to track/reconcile (array) |
| `Jira:AdditionalJql` | no | null | Extra JQL ANDed into the tracked-issue filter |
| `Jira:UseFake` | no | auto | Force the in-memory fake client. When unset, the fake is used **only if** credentials are missing |

**Fake vs real:** with no `BaseUrl`/`Email`/`ApiToken`, the service runs against an in-memory
fake Jira (seeded with `DAVE-1`, `DAVE-2`) so it is runnable with zero setup. Provide all three
to use the real REST client automatically. Set `Jira:UseFake=true` to force fake even with
credentials present.

### `Webhook` section

| Key | Required | Default | Description |
|---|---|---|---|
| `Webhook:Secret` | prod: yes | empty | Shared secret required on inbound webhooks. **If empty, the secret check is skipped** (local-only convenience). Always set in shared environments |

### `Sync` section

| Key | Default | Description |
|---|---|---|
| `Sync:ReconciliationInterval` | `00:05:00` | How often the reconciliation sweep runs |
| `Sync:ReconciliationOverlap` | `00:02:00` | Window overlap to tolerate clock skew / dropped events |
| `Sync:BackfillOnStartup` | `true` | Mirror all tracked issues once at startup |
| `Sync:OutboxPollInterval` | `00:00:15` | How often the write-back outbox drains |
| `Sync:MaxWriteBackAttempts` | `8` | Delivery attempts before a record is marked permanently failed |

### Database

| Key | Default | Description |
|---|---|---|
| `ConnectionStrings:JiraSync` | `Data Source=jirasync.db` | SQLite connection string (a file path) |

Migrations are applied **automatically on startup**. The default is a local SQLite file in the
working directory; point `ConnectionStrings:JiraSync` at a persistent volume in production
(e.g. `Data Source=/data/jirasync.db`).

---

## Creating a Jira API token

1. Sign in as the service account at <https://id.atlassian.com/manage-profile/security/api-tokens>.
2. **Create API token**, label it (e.g. `jira-sync-core`), copy the value.
3. Supply it as `Jira__ApiToken` (env var / secret store). Rotate by creating a new token and
   updating the secret; revoke the old one.

## Registering the Jira webhook

1. In Jira: **Settings → System → Webhooks → Create a webhook**.
2. URL: `https://<your-host>/webhooks/jira` — append the secret as `?secret=<Webhook:Secret>`,
   or send it as the `X-Webhook-Secret` request header.
3. Events: **Issue created, updated, deleted** and **Comment created**.
4. Requests without a valid secret are rejected with **401** (unless no secret is configured).

> The webhook endpoint must be reachable over **HTTPS** from Jira Cloud. Terminate TLS at your
> ingress/reverse proxy and forward to the service.

---

## Endpoints

| Path | Purpose |
|---|---|
| `GET /health` | Liveness/readiness check (`{ "status": "ok" }`) |
| `POST /webhooks/jira` | Jira webhook intake (secret-protected) |
| `GET /workitems`, `GET /workitems/{key}` | Inspect mirrored work items |
| `POST /workitems/{key}/writeback`, `GET /writeback` | Queue/inspect write-back |
| `GET /mappings`, `POST /mappings` | Resource ↔ work-item links |
| `GET /swagger` | API explorer (root `/` redirects here) |
| `GET /debug/jira-comments` | Fake-mode only: view comments the fake Jira "received" |

---

## Running

### Local

```bash
dotnet run --project src/SorryDave.JiraSync.Api --urls http://localhost:5050
```

Local secrets (dev only) via user-secrets:

```bash
dotnet user-secrets set "Jira:BaseUrl" "https://your-org.atlassian.net" --project src/SorryDave.JiraSync.Api
dotnet user-secrets set "Jira:Email"   "svc@your-org.com"               --project src/SorryDave.JiraSync.Api
dotnet user-secrets set "Jira:ApiToken" "<token>"                       --project src/SorryDave.JiraSync.Api
dotnet user-secrets set "Webhook:Secret" "<shared-secret>"             --project src/SorryDave.JiraSync.Api
```

### Server / container (environment variables)

```bash
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://0.0.0.0:8080
export ConnectionStrings__JiraSync="Data Source=/data/jirasync.db"
export Jira__BaseUrl="https://your-org.atlassian.net"
export Jira__Email="svc@your-org.com"
export Jira__ApiToken="<token>"            # from a secret store
export Jira__ProjectKeys__0="MDP"
export Webhook__Secret="<shared-secret>"   # from a secret store
dotnet SorryDave.JiraSync.Api.dll
```

### Deployment notes

- Put the service behind a reverse proxy / ingress that terminates HTTPS (required for the Jira webhook).
- Mount a persistent volume for the SQLite file, or change the connection string to a managed store.
- Inject `Jira__ApiToken` and `Webhook__Secret` from your platform's secret manager — not env files in source control.
- `GET /health` is suitable for liveness/readiness probes.
