# im-sorry-dave

A platform that keeps **Jira as the source of truth** for software work while letting the
team converse in Slack, code in GitHub, and plan in OpenSpec — with Claude summarizing
conversations back into Jira. Planned capabilities live as OpenSpec changes under
[`openspec/changes`](openspec/changes).

## Status

| Capability (OpenSpec change) | State |
|---|---|
| `jira-sync-core` | **Implemented** (this solution) |
| `slack-channel-provisioning` | Proposed |
| `slack-conversation-summarization` | Proposed |
| `github-work-item-linking` | Proposed |
| `openspec-jira-linking` | Proposed |

## jira-sync-core

ASP.NET Core service (.NET 10) that mirrors Jira work items, ingests Jira webhooks, runs a
reconciliation sweep, and writes decisions/answers/summaries back to Jira idempotently
through an outbox. Persistence is SQLite (file `jirasync.db`, created on first run).

### Projects

- `src/SorryDave.JiraSync.Core` — domain model, persistence, Jira client, sync, mapping, write-back.
- `src/SorryDave.JiraSync.Api` — HTTP host: webhook intake + review/admin endpoints + Swagger.
- `tests/SorryDave.JiraSync.Tests` — unit tests (stale-event guard, idempotent write-back, mapping uniqueness, outbox sender).

### Run it locally (no Jira account needed)

```bash
dotnet run --project src/SorryDave.JiraSync.Api --urls http://localhost:5050
```

With no Jira credentials configured, the service uses an **in-memory fake Jira client**
seeded with two issues (`DAVE-1`, `DAVE-2`). On startup it backfills them, so you can
exercise the whole pipeline immediately. Open <http://localhost:5050/swagger>.

Try the loop:

1. `GET /workitems` — see the backfilled issues.
2. `POST /workitems/DAVE-1/writeback` — queue a decision:
   ```json
   { "recordIdentity": "decision-001", "kind": "Decision",
     "content": "Team agreed: open the pod bay doors at 0600.",
     "sourceUrl": "https://slack.example/archives/C1/p123", "author": "Dave Bowman" }
   ```
3. `GET /writeback` — watch it move `Pending` → `Sent` (the outbox drains every 15s).
4. `GET /debug/jira-comments` — see the comment the fake Jira "received", including the
   `[managed-record:...]` idempotency marker.
5. `POST /webhooks/jira` — post a Jira webhook payload to drive a live update (see below).

### Connecting real Jira

Set these (use user-secrets / a secret store — never commit tokens):

```bash
dotnet user-secrets set "Jira:BaseUrl" "https://your-org.atlassian.net" --project src/SorryDave.JiraSync.Api
dotnet user-secrets set "Jira:Email" "you@example.com" --project src/SorryDave.JiraSync.Api
dotnet user-secrets set "Jira:ApiToken" "<api-token>" --project src/SorryDave.JiraSync.Api
dotnet user-secrets set "Webhook:Secret" "<shared-secret>" --project src/SorryDave.JiraSync.Api
```

Set `Jira:ProjectKeys` in `appsettings.json` to the tracked project(s). When credentials are
present the real REST client is used automatically (override with `Jira:UseFake`).

**Webhook registration:** in Jira, register a webhook pointing at
`https://<host>/webhooks/jira` for *Issue created/updated/deleted* and *Comment created*.
Append the shared secret as `?secret=<shared-secret>` or send it as the `X-Webhook-Secret`
header. Requests without a valid secret are rejected with 401 (the check is skipped only when
no secret is configured).

### Tests

```bash
dotnet test
```

## Aspire AppHost (`aspire-apphost`)

A [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) AppHost orchestrates the solution:
it starts the **API**, waits until the API's `/health` reports healthy, then makes the
smoke-test **console** available — with a dashboard for logs and health.

```bash
dotnet run --project src/SorryDave.JiraSync.AppHost
```

This launches the API and the Aspire dashboard (the URL is printed on startup). The API uses
its own configuration — with the API project's user-secrets it tracks real Jira (`MDP`).

The `console` resource is registered with **explicit start**. A Terminal.Gui UI cannot run inside
Aspire's process host (DCP redirects stdio, so it can't initialize a console), so the resource is
an executable that — when you press ▶ on it in the dashboard — **opens the TUI in a new terminal
window**. It waits for the API and inherits the API endpoint via service discovery. (The dashboard
marks the resource finished immediately, because the launcher returns as soon as the window opens.)

You can also run the TUI yourself in a terminal; it falls back to `http://localhost:5050` when not
launched by the AppHost:

```bash
dotnet run --project src/SorryDave.JiraSync.SmokeTui
```

`SorryDave.JiraSync.ServiceDefaults` wires the API's health/OpenTelemetry/service-discovery
defaults so the AppHost's health-gated ordering has a definite readiness signal.

## Smoke-test TUI (`tui-smoke-test`)

An interactive [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) console app for eyes-on
smoke testing. It is a **client of the running API** (it does not use Core in-process) and
shows whatever backend the API is configured for.

```bash
dotnet run --project src/SorryDave.JiraSync.SmokeTui
```

> Run it in a real terminal (it takes over the console) — not through a redirected/headless pipe.

It targets the API base address from configuration (`services:api:http:0` injected by Aspire,
or `ApiBaseUrl`), defaulting to `http://localhost:5050`. The status bar shows `API: <url>`. So
start the API first (directly or via the AppHost), then run the console.

What you can do (all via the API):

- **Backfill** — `POST /admin/backfill` to mirror tracked work items.
- **Simulate webhook** — posts a sample `jira:issue_updated` event to `POST /webhooks/jira`.
- **Submit write-back** — posts a decision and drains the outbox (`POST /admin/drain-writeback`); watch it reach `Sent`.
- **Guided smoke run** (menu *Run → Guided smoke run*, or `Ctrl-R`) — backfill → write-back → deliver → verify, reported per-step **PASS/FAIL**.

Keys: `F5` refresh · `Ctrl-R` smoke run · `Ctrl-Q` quit. The guided-run logic is covered
headlessly by `JiraSyncSmokeRunnerTests` (using an in-memory fake API client).
