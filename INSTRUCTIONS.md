# Deployment playbook

A zero-to-verified guide for an administrator deploying **im-sorry-dave** (the Jira-sync platform
with Slack channel provisioning) to AWS. Follow the phases in order.

The Aspire/CDK deployment mechanics are infrastructure-as-code and are **not** re-documented here —
`aspire deploy` is referenced as a single step (see `openspec/specs/aws-deployment` for the IaC
detail). This guide covers everything *around* it: accounts, permissions, API keys, secret
provisioning, the smoke-test TUI, and a manual verification walkthrough.

Throughout, secrets are shown as placeholders (`<token>`, `xoxb-…`) — never paste real values into
files or commits.

---

## Phase 0 — Prerequisites & accounts

Gather these before starting:

- **AWS account** with credentials configured locally (`aws configure`). The first deploy needs
  broad permissions (VPC, ECS, ELB, EFS, ACM, Route 53, SSM, IAM, ECR, CloudFormation).
- **Atlassian / Jira Cloud site** (e.g. `https://your-org.atlassian.net`) with a project to track.
- **Slack workspace** where you can create and install an app (admin approval may be required).
- A **registered domain with a Route 53 public hosted zone** (this deployment uses
  `jsg.appcloud.systems` in zone `appcloud.systems`). Needed for the ACM cert + public HTTPS.
- **Local toolchain:** .NET 10 SDK, Docker, Node + AWS CDK CLI (`npm i -g aws-cdk`), the Aspire CLI,
  and the AWS CLI. One-time per account/region: `cdk bootstrap aws://<account>/us-east-1`.

This deployment runs in **us-east-1** (account `991795635857`, stack `aws2` in the reference setup).

---

## Phase 1 — Jira

### 1.1 API token and account permissions

1. Create an API token at **id.atlassian.com → Security → API tokens**. The service authenticates as
   the **owning user** via Basic auth (email + token).
2. That account needs, on the tracked project:
   - **Browse Projects** — to mirror issues.
   - **Add Comments** — write-back posts managed comments.
3. **Registering the webhook requires a Jira admin** (System → WebHooks, or the REST API below).

### 1.2 Configuration and webhook registration

Non-secret Jira config (set as env in the AppHost / `appsettings.json`; the token is a secret — see
Phase 3):

| Key | Value |
|---|---|
| `Jira:BaseUrl` | `https://your-org.atlassian.net/` |
| `Jira:Email` | the token owner's email |
| `Jira:ProjectKeys` | the tracked project(s), e.g. `["MDP"]` |
| `Jira:ApiToken` | **secret** (Phase 3) |

Register the inbound webhook (Jira **Settings → System → WebHooks**, or the REST API):

```bash
POST https://<your-site>/rest/webhooks/1.0/webhook
{ "name": "im-sorry-dave jira-sync",
  "url": "https://jsg.appcloud.systems/webhooks/jira?secret=<webhook-secret>",
  "events": ["jira:issue_created","jira:issue_updated","jira:issue_deleted","comment_created"],
  "filters": { "issue-related-events-section": "project = MDP" } }
```

The `?secret=` must match the deployed `Webhook:Secret` (Phase 3) — the endpoint returns **401**
without it. Scope the filter to your project.

### 1.3 Jira Cloud email limitation

Jira **Cloud hides user email addresses** (GDPR), so resolving Slack identities by email does **not**
work there. Slack invites resolve via `Slack:UserMap` (Jira accountId or displayName → Slack id) and
the fixed `Slack:InviteUserIds` list instead (Phase 2). On Jira **Data Center**, email is available
and an email-based resolver could be added.

---

## Phase 2 — Slack

### 2.1 Create the app and bot scopes

Create the app from the manifest in **`docs/slack-app-manifest.yaml`**
(https://api.slack.com/apps → Create New App → From an app manifest). It requests these **bot
scopes**:

`channels:manage` · `channels:read` · `chat:write` · `pins:write` · `users:read` · `users:read.email`

Use a **test workspace** first — provisioning creates *real* channels.

### 2.2 Tokens and the operator's Slack id

1. **Install to Workspace** (approve scopes). This mints the **Bot User OAuth Token** under
   *OAuth & Permissions* → `xoxb-…` → config key **`Slack:BotToken`**.
2. **Signing secret**: *Basic Information → App Credentials → Signing Secret* → **`Slack:SigningSecret`**.
3. Find your own Slack user id (for `InviteUserIds`) — *Slack profile → Copy member ID*, or:
   `POST https://slack.com/api/users.list` with `Authorization: Bearer xoxb-…`.

### 2.3 Non-secret Slack config

In `appsettings.json` (`Slack` section; committed, not secret):

| Key | Purpose |
|---|---|
| `Slack:EligibleIssueTypes` | issue types that auto-provision a channel, e.g. `["Idea"]`. **Empty = every new item gets a channel** — set it to keep sprawl in check. |
| `Slack:InviteUserIds` | Slack ids invited to every provisioned channel (e.g. your own id). |
| `Slack:UserMap` | Jira accountId/displayName → Slack id, for inviting the creator / assignee / mentioned users. |
| `Slack:AutoInvite` | master switch for auto-inviting (default `true`). |

With no `Slack:BotToken`, the Slack integration is **dormant** (provisioning reports "not configured").

---

## Phase 3 — Secrets & keys

**All secrets are configuration keys** the app reads source-agnostically (see
`secrets-configuration` spec). The layering, lowest-to-highest precedence:

```
appsettings.json (non-secret defaults)  →  SSM /jira-sync/* (prod)  →  env vars  →  user-secrets (dev)
```

### Secret inventory

| Config key | Local (debug) | AWS (prod) | How it reaches AWS |
|---|---|---|---|
| `Jira:ApiToken` | API user-secrets | SSM `/jira-sync/Jira/ApiToken` | `aws ssm put-parameter` |
| `Webhook:Secret` | API user-secrets | SSM `/jira-sync/Webhook/Secret` | `aws ssm put-parameter` |
| `Slack:BotToken` | API + AppHost user-secrets | SSM `/jira-sync/Slack/BotToken` | **AppHost transport** on deploy |
| `Slack:SigningSecret` | API + AppHost user-secrets | SSM `/jira-sync/Slack/SigningSecret` | **AppHost transport** on deploy |
| `Anthropic:ApiKey` *(future)* | user-secrets | SSM `/jira-sync/Anthropic/ApiKey` | tbd |

In AWS the API loads these via an app-side **SSM Parameter Store** provider over the `/jira-sync/`
prefix, authorized by a single task-role grant — so adding a secret is just another parameter (no new
IAM grant, no redeploy).

### Local development

```bash
dotnet user-secrets set "Jira:BaseUrl"      "https://your-org.atlassian.net/" --project src/SorryDave.JiraSync.Api
dotnet user-secrets set "Jira:Email"        "you@example.com"                 --project src/SorryDave.JiraSync.Api
dotnet user-secrets set "Jira:ApiToken"     "<token>"                          --project src/SorryDave.JiraSync.Api
dotnet user-secrets set "Webhook:Secret"    "<secret>"                         --project src/SorryDave.JiraSync.Api
dotnet user-secrets set "Slack:BotToken"    "xoxb-…"                           --project src/SorryDave.JiraSync.Api
dotnet user-secrets set "Slack:SigningSecret" "<signing>"                      --project src/SorryDave.JiraSync.Api
# Slack secrets ALSO go in the AppHost project so they transport to SSM on deploy:
dotnet user-secrets set "Slack:BotToken"      "xoxb-…"    --project src/SorryDave.JiraSync.AppHost
dotnet user-secrets set "Slack:SigningSecret" "<signing>" --project src/SorryDave.JiraSync.AppHost
```

### AWS — provision the SSM parameters

```bash
# Generate a strong webhook secret (example):  openssl rand -hex 32
aws ssm put-parameter --name "/jira-sync/Jira/ApiToken"  --type SecureString --value "<token>"  --region us-east-1
aws ssm put-parameter --name "/jira-sync/Webhook/Secret" --type SecureString --value "<secret>" --region us-east-1
# Slack secrets are transported automatically from the AppHost user-secrets on `aspire deploy`
# (no manual put-parameter needed) — to /jira-sync/Slack/BotToken and /jira-sync/Slack/SigningSecret.
```

> **The split, and a follow-up:** the Jira token + webhook secret are `put-parameter`'d directly,
> while Slack secrets ride the AppHost user-secrets → SSM transport. Unifying everything under the
> AppHost transport (so an admin sets all secrets in one place) is a possible future change.

---

## Phase 4 — Deploy & verify

Deploy from the AppHost (single command; the IaC provisions VPC/ECS/ALB/EFS/ACM/Route 53/SSM):

```bash
aspire deploy --project src/SorryDave.JiraSync.AppHost --non-interactive
```

> **Note:** the `aspire deploy` CLI can exit with an error/timeout while **CloudFormation keeps
> going**. Don't assume failure — check the stack reached `UPDATE_COMPLETE` and `/health` is good
> before retrying:
> `aws cloudformation describe-stacks --stack-name aws2 --query "Stacks[0].StackStatus" --output text`

Verify:

```bash
curl https://jsg.appcloud.systems/health      # -> Healthy
curl https://jsg.appcloud.systems/workitems   # -> your real Jira issues
# Webhook is secured:
curl -X POST https://jsg.appcloud.systems/webhooks/jira                 # -> 401 (no secret)
curl -X POST "https://jsg.appcloud.systems/webhooks/jira?secret=<secret>" \
  -H "Content-Type: application/json" -d '{"webhookEvent":"jira:issue_updated","issue":{"key":"MDP-1","fields":{"summary":"x","updated":"2026-01-01T00:00:00.000+0000"}}}'  # -> 200
```

---

## Phase 5 — Smoke-test TUI setup (optional)

> **The TUI is optional and is never deployed.** It's a local operator tool — a Terminal.Gui console
> that drives the API over HTTP — handy for **smoke-testing different areas** of the platform
> (work-item sync, write-back, Slack provisioning) against whichever target you select. The platform
> runs fully without it; skip this phase if you don't need hands-on verification.

The console drives one of several configured **targets** (`appsettings.json` of
`SorryDave.JiraSync.SmokeTui`):

```jsonc
"ApiTargets": { "local": { "BaseUrl": "http://localhost:5050" },
                "aws":   { "BaseUrl": "https://jsg.appcloud.systems" } },
"ActiveApiTarget": "local"
```

The deployed webhook is secured, so the `aws` target needs the secret for "Simulate webhook":

```bash
# value from SSM /jira-sync/Webhook/Secret
dotnet user-secrets set "ApiTargets:aws:WebhookSecret" "<secret>" --project src/SorryDave.JiraSync.SmokeTui
```

Launch:

- **Via the AppHost (recommended):** `dotnet run --project src/SorryDave.JiraSync.AppHost`, then press
  ▶ on the **console** resource in the dashboard. The injected API endpoint folds into the `local`
  target, so the Target menu is simply **local** (the running API) vs **aws**.
- **Standalone:** `dotnet run --project src/SorryDave.JiraSync.SmokeTui --target aws`.

Switch targets at runtime from the **Target** menu; the status bar shows `Target: <name> (<url>)`.

---

## Phase 6 — Administrator smoke-test walkthrough

Run these from the TUI, in order. Each step: **Action → Expected → Verify where.**

1. **Backfill / list** — *Backfill*, then the work-item list populates.
   → Expected: your real Jira issues (e.g. `MDP-1…N`). → Verify: the list in the TUI matches Jira.
2. **Simulate webhook** — select an item → *Simulate webhook*.
   → Expected: the item's status flips to `In Review` in the mirror. → Verify: the list refreshes;
   if the item has a Slack channel, a `Status → In Review` note appears there. *(This changes the
   mirror, not Jira.)*
3. **Submit write-back** — select an item → *Submit write-back*, enter text.
   → Expected: a record reaches `Sent` in the outbox view. → Verify: the comment appears on the Jira
   issue (with a `[managed-record:…]` marker).
4. **Slack → Provision channel** — select an item → *Slack → Provision channel*.
   → Expected: `Created: #<key>-<slug>`. → Verify in Slack: a public channel exists with a **pinned**
   title+Jira-link message, a description message, and you (from `InviteUserIds`) as a member.
5. **Slack → Show linked channel** — → Expected: prints the linked channel name. → Verify it matches.
6. **Slack → Archive / Unarchive channel** — → Expected: the channel archives (with a closing note),
   then unarchives. → Verify in Slack.
7. **Switch Target → aws** — repeat *list* and *Simulate webhook* against the live instance.
   → Expected: the AWS API lists real issues and accepts the simulated webhook (the target carries
   the secret). → Verify the status bar shows `Target: aws`.

**Mentions / auto-provision (real Jira):** create a new item of an eligible type → a channel
auto-provisions and the creator is invited. @mention a mapped user in the description or a comment →
they're invited and get a welcome message containing the mention text.

---

## Phase 7 — Troubleshooting (known gotchas)

- **SSM `GetParametersByPath` AccessDenied** — the grant must cover **both** the path node
  (`parameter/jira-sync`) and the children (`parameter/jira-sync/*`); a `/*`-only grant fails the
  by-path call. (Handled in the AppHost; relevant if you hand-edit IAM.)
- **ECS tasks crash-loop with SQLite "unable to open database file"** — the container runs non-root
  with a read-only working dir; SQLite must live on the writable EFS mount (`/data`). Already
  configured.
- **ALB target unhealthy** — the health check hits `/` expecting **200**; the API root returns 200
  (Swagger is at `/swagger`). Don't make `/` redirect.
- **Deploy fails: "AvailabilityZoneRebalancing does not support maximumPercent <= 100"** — AZ
  rebalancing must be **disabled** for the single-instance, non-overlapping (stop-then-start) deploy.
  Already set.
- **`aspire deploy` exits with an error but the app is fine** — the CLI can time out while
  CloudFormation continues. Check the stack status and `/health` before retrying.
- **A provisioned Slack channel isn't visible** — public channels you haven't joined don't show in
  the sidebar. Put your Slack id in `Slack:InviteUserIds` (newly-provisioned channels then invite
  you), or **Browse channels → Join**.
- **A comment @mention didn't invite the person** — the person must be a Jira user **and** mapped in
  `Slack:UserMap`, and the item must already have a channel. The webhook often delivers the comment
  body as rendered text (no accountId), so the service fetches the comment ADF via REST to resolve
  the mention — this needs the item's `comment_created` webhook to be registered.
- **Jira Cloud user email is private** — by-email Slack identity resolution doesn't work on Cloud;
  use `Slack:UserMap` / `Slack:InviteUserIds`.

---

## Deploying to Azure (alternative to AWS)

The same codebase can deploy to **Azure** instead of AWS, selected at deploy time. It mirrors the AWS
shape: a single container, SQLite on a mounted file share, a managed secret store, and public HTTPS.

> **Status:** the Azure path is defined by the **`azure-deployment`** change
> (`openspec/changes/azure-deployment`). Until that change is applied, the `--cloud azure` selector
> and the Azure branch below are **not yet built** — this section is the target procedure. The AWS
> path (the default) is the one in use today.

### Azure prerequisites

- An **Azure subscription** and the **Azure CLI**, signed in: `az login` (the analog of
  `aws configure`).
- The Aspire Azure tooling (`Aspire.Hosting.Azure.*` packages — added by the `azure-deployment`
  change).
- A DNS zone only if you want a custom domain; the default Azure Container Apps endpoint already
  provides managed HTTPS.

### What gets created

- **Azure Container Apps** — runs the API as a **single replica** (SQLite is single-writer, same
  constraint as the AWS single instance).
- **Azure Files** — mounted at `/data` for the SQLite database (the EFS analog); the connection
  string stays `Data Source=/data/jirasync.db`.
- **Azure Key Vault** — the secret store (the SSM analog). The container authenticates with its
  **managed identity**; secret names map `--` → `:` (`Jira--ApiToken` → `Jira:ApiToken`).
- **Managed HTTPS** — Container Apps external ingress gives an HTTPS `*.azurecontainerapps.io`
  endpoint with a managed certificate (no ACM/Route 53 analog needed for TLS).

### Deploy & secrets

```bash
# Secrets are transported from the AppHost's local config into Key Vault on deploy (the SSM-transport
# analog); set them in user-secrets as in Phase 3, then:
aspire deploy --project src/SorryDave.JiraSync.AppHost --non-interactive --cloud azure
```

The app reads the **same config keys** as on AWS — only the provider differs (Azure Key Vault instead
of SSM Parameter Store). The fail-fast-on-missing-secret behavior is unchanged.

### Verify & TUI

Verification is identical to Phase 4, against the Azure endpoint (`/health`, `/workitems`, webhook
401/200). Point the smoke-test TUI at Azure by adding an `azure` target to its `ApiTargets`
(the ACA URL) plus its `ApiTargets:azure:WebhookSecret` user-secret, then select it from the Target
menu.

### Azure differences from AWS (quick reference)

| | AWS | Azure |
|---|---|---|
| Compute | ECS Fargate + ALB | Azure Container Apps |
| File mount | EFS | Azure Files |
| Secret store | SSM Parameter Store | Azure Key Vault |
| HTTPS | ACM cert + Route 53 on a custom domain | managed cert on the ACA endpoint |
| Auth to secrets | task-role IAM grant | container managed identity |

---

## Teardown (stops all cost)

```bash
aws cloudformation delete-stack --stack-name aws2 --region us-east-1
```

The EFS file system uses `RemovalPolicy.DESTROY`, so its data is deleted with the stack — switch to
`RETAIN` first if you need to keep anything you can't re-backfill from Jira. Running cost is roughly
**$85/month** (NAT gateways dominate).
