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
