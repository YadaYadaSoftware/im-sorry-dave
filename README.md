# im-sorry-dave

A platform that keeps **Jira as the source of truth** for software work while letting the
team converse in Slack, code in GitHub, and plan in OpenSpec — with Claude summarizing
conversations back into Jira. Planned capabilities live as OpenSpec changes under
[`openspec/changes`](openspec/changes).

## Status

| Capability (OpenSpec change) | State |
|---|---|
| `jira-sync-core` | **Implemented** (archived) |
| `aspire-apphost` | **Implemented** (archived) — AppHost + ServiceDefaults + console-as-API-client |
| `tui-smoke-test` | **Implemented** (archived) — interactive smoke TUI |
| `slack-channel-provisioning` | Proposed (decision-complete) |
| `slack-conversation-summarization` | Proposed (decision-complete) |
| `aws-aspire-deployment` | Proposed — deploy via `aspire deploy` to AWS; **Azure is the eventual home** (portable container + PostgreSQL) |
| `github-work-item-linking` | Proposed |
| `openspec-jira-linking` | Proposed |
| `console-control-app` | Proposed |

> **Hosting direction:** AWS is the interim deployment target; the platform is **eventually
> destined for Azure**. The deployment is kept portable (provider-agnostic container + PostgreSQL)
> so moving to Azure Container Apps + Azure Database for PostgreSQL — or falling back to Azure if
> the AWS `aspire deploy` path stalls — is low-friction. See `openspec/changes/aws-aspire-deployment`.

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

## Deploying to AWS (`aws-aspire-deployment`)

The Aspire AppHost deploys the **API** (only) to AWS ECS Fargate via `aspire deploy`. Live at
**https://jsg.appcloud.systems**. The interactive console is never deployed (it's added only in
`builder.ExecutionContext.IsRunMode`).

### What gets created (stack `aws2`, region `us-east-1`)
ECR image · VPC (2 AZ, 2 NAT gateways) · ECS Fargate cluster + service (**1 task**) · ALB with
HTTP:80 + HTTPS:443 · ACM cert for `jsg.appcloud.systems` (DNS-validated) · Route 53 alias ·
**EFS** file system mounted at `/data` for the SQLite DB · CloudWatch logs.

> **Cost:** ~**$85/month** while running (NAT gateways dominate) + negligible EFS.

### Prerequisites
- AWS account + credentials: `aws configure` (an admin-ish user for the first deploy).
- Tools: **Docker**, **Node + AWS CDK CLI** (`npm i -g aws-cdk`), the **Aspire CLI**, .NET 10 SDK.
- A **Route 53 public hosted zone** for the domain (here `appcloud.systems`).
- One-time per account/region: `cdk bootstrap aws://<account>/us-east-1`.

### Secrets (one-time)
The Jira API token is read from **AWS Secrets Manager** at runtime (never in the image/repo):
```bash
aws secretsmanager create-secret --name "jira-sync/jira-api-token" \
  --secret-string "<jira-api-token>" --region us-east-1
```
Non-secret Jira config (`Jira__BaseUrl`, `Jira__Email`, `Jira__ProjectKeys__0=MDP`) and the EFS
DB path are set as env vars in the AppHost's publish branch.

### Deploy / update
```bash
# The webhook shared secret is passed at deploy time as a plain env var (see "secure the webhook").
$env:WebhookSecret = (aws secretsmanager get-secret-value --secret-id jira-sync/webhook-secret --query SecretString --output text)
aspire deploy --project src/SorryDave.JiraSync.AppHost --non-interactive
```
`aspire publish` (no deploy) synthesizes the CDK to `aws-publish/cdk.out/` if you want to inspect
the CloudFormation first. Re-running `aspire deploy` updates the stack in place. Because the
service is single-writer (EFS+SQLite), deploys are **stop-then-start** (brief downtime; AZ
rebalancing is disabled so `MaximumPercent=100` is allowed).

> **Important:** every `aspire deploy` must set `$env:WebhookSecret`, or the API redeploys with an
> empty secret and the webhook endpoint goes back to accepting unsigned requests.

### Verify
```bash
curl https://jsg.appcloud.systems/health      # -> Healthy
curl https://jsg.appcloud.systems/workitems   # -> MDP-1 .. MDP-7 (real Jira)
```

### Register the Jira webhook
Point Jira at the deployed endpoint (Jira **Settings → System → WebHooks**, or the REST API):
```bash
POST https://<site>/rest/webhooks/1.0/webhook
{ "name": "im-sorry-dave jira-sync (MDP)",
  "url": "https://jsg.appcloud.systems/webhooks/jira",
  "events": ["jira:issue_created","jira:issue_updated","jira:issue_deleted","comment_created"],
  "filters": { "issue-related-events-section": "project = MDP" } }
```
**The webhook is secured.** A shared secret lives in Secrets Manager (`jira-sync/webhook-secret`)
and is supplied at deploy time as `$env:WebhookSecret` → injected as the API's `Webhook__Secret`
env var; the registered webhook URL carries `?secret=<value>`. Requests without the secret get
**401**. (It's passed as a plain env var rather than an ECS-injected Secrets Manager secret — see
the learnings below for why.)

### Teardown (stops all cost)
```bash
aws cloudformation delete-stack --stack-name aws2 --region us-east-1
```
The EFS file system has `RemovalPolicy.DESTROY`, so its data is deleted with the stack — switch to
`RETAIN` before relying on it for anything you can't re-backfill from Jira.

### Gotchas baked into the AppHost (learnings)
- The container runs non-root with a read-only working dir → **SQLite must live on a writable path** (the EFS `/data` mount).
- The ALB health check hits `/` expecting **200** → the API root returns 200 (Swagger is at `/swagger`).
- ECS **Availability Zone Rebalancing** forbids `MaximumPercent <= 100`; it's disabled so the single-instance, non-overlapping deploy config is valid.
- **Adding a *new* ECS-injected Secrets Manager secret in a stop-then-start deploy can outage the service:** the new task launches before the IAM `GetSecretValue` grant has propagated, and with `MinHealthyPercent=0` there's no old task to fall back on, so the rollout spins on `AccessDeniedException`. The Jira token (already-propagated grant) is injected this way fine, but the webhook secret is passed as a **plain env var** to sidestep this.
- **Eventual Azure:** EFS+SQLite is AWS-locked; the move to Azure Container Apps + Azure Database for PostgreSQL would switch persistence then.
