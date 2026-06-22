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
| `tui-api-target-selection` | **Implemented** (archived) — local/aws target switching |
| `aws-aspire-deployment` | **Implemented** (archived) — live on AWS |
| `secrets-configuration-convention` | **Implemented** (archived) — SSM Parameter Store |
| `slack-channel-provisioning` | **Implemented** (archived) — channel per work item, lifecycle, invites |
| `slack-auto-provision` | **Implemented** — auto-provision on item creation; creator + mentions invited |
| `slack-conversation-summarization` | Proposed (decision-complete) |
| `aws-aspire-deployment (Azure)` | Proposed (`azure-deployment`) — also deploy to Azure (ACA + Azure Files + Key Vault) |
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

### Choosing the API target (`tui-api-target-selection`)

The console drives one of several configured **targets** — `local` and `aws` ship in
`appsettings.json`:

```jsonc
"ApiTargets": {
  "local": { "BaseUrl": "http://localhost:5050" },
  "aws":   { "BaseUrl": "https://jsg.appcloud.systems" }
},
"ActiveApiTarget": "local"
```

**Recommended flow — start the AppHost, then choose in the TUI:**

```bash
dotnet run --project src/SorryDave.JiraSync.AppHost
```

Press ▶ on the **console** resource in the dashboard. The AppHost injects its API endpoint into the
TUI, which **folds it into the `local` target** — so the TUI's Target menu is simply **`local`** (the
AppHost's running API) vs **`aws`** (the deployed one). No second startup, no juggling ports.

- **Switch at runtime:** the **Target** menu lists the targets; selecting one reconnects the panel.
  The status bar shows `Target: <name> (<url>)`.
- **Pick at launch (standalone):** `dotnet run --project src/SorryDave.JiraSync.SmokeTui --target aws`.
- **Selection order:** `--target` > `ActiveApiTarget` > `local` (the AppHost-injected endpoint, when present).

The deployed AWS webhook is **secured**, so "Simulate webhook" against `aws` must send the shared
secret. Put it in the console's user-secrets (never the repo) — it travels with the target:

```bash
# value from SSM /jira-sync/Webhook/Secret
dotnet user-secrets set "ApiTargets:aws:WebhookSecret" "<secret>" --project src/SorryDave.JiraSync.SmokeTui
```

List/backfill/write-back work against either target; only "Simulate webhook" needs the secret (the
`local` API accepts unsigned requests). Start the local API first if you're targeting `local`.

What you can do (all via the API):

- **Backfill** — `POST /admin/backfill` to mirror tracked work items.
- **Simulate webhook** — posts a sample `jira:issue_updated` event to `POST /webhooks/jira`.
- **Submit write-back** — posts a decision and drains the outbox (`POST /admin/drain-writeback`); watch it reach `Sent`.
- **Guided smoke run** (menu *Run → Guided smoke run*, or `Ctrl-R`) — backfill → write-back → deliver → verify, reported per-step **PASS/FAIL**.

Keys: `F5` refresh · `Ctrl-R` smoke run · `Ctrl-Q` quit. The guided-run logic is covered
headlessly by `JiraSyncSmokeRunnerTests` (using an in-memory fake API client).

## Slack channels (`slack-channel-provisioning`, `slack-auto-provision`)

Each tracked work item gets its own Slack channel, mirroring the item's lifecycle.

- **Auto-provisioned on creation.** When a new work item is created (the `jira:issue_created`
  webhook), an eligible item automatically gets a public channel named `<jira-key>-<summary-slug>`.
  Eligibility is scoped by `Slack:EligibleIssueTypes` (e.g. `["Idea"]`); leaving it empty makes
  **every** new item provision a channel — set it to keep channel sprawl in check. Explicit
  provisioning (TUI **Slack → Provision**, or `POST /slack/{key}/provision`) still works and is
  idempotent.
- **Welcome messages.** Two messages are posted: a **pinned** header with the item's title and a Jira
  link, then a second message with the description.
- **Invites.** The configured `Slack:InviteUserIds`, the assignee, the item's **creator** (reporter),
  and anyone **@mentioned in the description or in a comment** are invited (resolved via
  `Slack:UserMap`; unresolved are skipped). Mention invites fire **whenever the mention occurs** — at
  creation and afterward (comments, description edits) — for items that have a channel. On Jira Cloud,
  user emails are private, so identities map via `UserMap` (Jira accountId or displayName → Slack id)
  rather than by email.
- **Lifecycle.** Status → closed archives the channel (with a closing note); reopen unarchives;
  status/assignee changes update the topic and post a note. A periodic sweep reconciles links against
  Slack (dangling links, archived-state drift); `POST /slack/reconcile` runs it on demand.

Config (`Slack` section): non-secret `EligibleIssueTypes`, `InviteUserIds`, `UserMap`, `AutoInvite`,
`ReconciliationInterval` in `appsettings.json`; `BotToken` + `SigningSecret` are secrets (user-secrets
locally, SSM `/jira-sync/Slack/*` in AWS). With no `BotToken` the integration is dormant. Create the
Slack app from `docs/slack-app-manifest.yaml`.

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

### Secrets (convention)
**All secrets are configuration keys** the API reads source-agnostically. In AWS they come from
**SSM Parameter Store** (SecureString) under the `/jira-sync/` prefix — parameter paths map to config
keys (`/jira-sync/Jira/ApiToken` → `Jira:ApiToken`). The API loads them at startup via an app-side
provider (enabled by `Aws__ParameterStorePath=/jira-sync`, set in the AppHost), and the **task role**
has one grant on the prefix, so adding a secret is just another parameter — no new IAM grant, no
task-def change, no redeploy.

```bash
# one-time per secret (SecureString, KMS-encrypted)
aws ssm put-parameter --name "/jira-sync/Jira/ApiToken"  --type SecureString --value "<token>"
aws ssm put-parameter --name "/jira-sync/Webhook/Secret" --type SecureString --value "<secret>"
# Slack/Claude land the same way: /jira-sync/Slack/BotToken, /jira-sync/Slack/SigningSecret,
#                                 /jira-sync/Anthropic/ApiKey
```

Precedence is layered: `appsettings.json` (non-secret defaults, **no secrets committed**) → SSM
(prod) → environment variables → user-secrets (local dev). Non-secret Jira config (`Jira__BaseUrl`,
`Jira__Email`, `Jira__ProjectKeys__0=MDP`) and the EFS DB path are plain env vars in the AppHost.
On the eventual **Azure** move, swap the SSM provider for the Azure Key Vault provider — the config
keys and app code don't change.

### Deploy / update
```bash
aspire deploy --project src/SorryDave.JiraSync.AppHost --non-interactive
```
Secrets come from SSM at runtime, so no secret env vars are needed at deploy time. `aspire publish`
(no deploy) synthesizes the CDK to `aws-publish/cdk.out/` if you want to inspect the CloudFormation
first. Re-running `aspire deploy` updates the stack in place. Because the service is single-writer
(EFS+SQLite), deploys are **stop-then-start** (brief downtime; AZ rebalancing is disabled so
`MaximumPercent=100` is allowed).

> **Note:** the `aspire deploy` CLI may exit with an error/timeout while CloudFormation keeps going —
> check the stack status (`UPDATE_COMPLETE`) and `/health` before assuming a deploy failed.

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
**The webhook is secured.** The shared secret lives in SSM (`/jira-sync/Webhook/Secret`) and the API
loads it as `Webhook:Secret`; the registered webhook URL carries `?secret=<value>`. Requests without
the secret get **401**.

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
- **Secrets come from SSM Parameter Store via an app-side provider, not ECS secret injection.** ECS injection needs a *new* IAM grant per secret, and adding one during a `MinHealthyPercent=0` stop-then-start deploy outages the service (the new task launches before the grant propagates, with no old task to fall back on). The SSM provider needs one task-role grant on the prefix, so new secrets are additive — see the secrets section above.
- **`ssm:GetParametersByPath` authorizes against the path NODE, not the child wildcard.** Grant **both** `parameter/jira-sync` *and* `parameter/jira-sync/*` — a `/*`-only grant fails the by-path call with `AccessDenied` even though the CLI by-name calls work.
- **The SSM provider must be added directly to the builder** (`builder.Configuration.AddSystemsManager(...)`). Building the source on a throwaway `ConfigurationBuilder` and re-homing it loads nothing (no error, just empty config). Also: Git Bash mangles a `/jira-sync`-style env-var value (MSYS path conversion) — test the provider from PowerShell, or prefix `MSYS_NO_PATHCONV=1`.
- **Eventual Azure:** EFS+SQLite is AWS-locked, and the SSM provider is AWS-specific (swap it for the Key Vault provider — config keys unchanged). The move to Azure Container Apps + Azure Database for PostgreSQL would switch persistence then.
