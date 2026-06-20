## Why

The platform is about to gain three more secrets (Slack bot token, Slack signing secret, Anthropic
API key) on top of the two it has (Jira API token, webhook secret). The current ad-hoc delivery —
the Jira token via **ECS task-definition secret injection** and the webhook secret via a **plain env
var** — has no convention, and we learned the hard way that adding a *new* ECS-injected secret
during a single-instance stop-then-start deploy **takes the service down** (the new task launches
before its IAM grant propagates, with no old task to fall back on). We want one secrets convention,
settled before Slack and Claude land, that makes adding a secret a non-event.

## What Changes

- Establish that **all secrets are plain `IConfiguration` keys** the API reads source-agnostically;
  the *only* thing that varies per environment is which provider populates the key.
- Define a **layered precedence**: committed `appsettings.json` (non-secret structure/defaults) →
  the managed secret store (production) → environment variables (override) → user-secrets (dev).
- In AWS, load production secrets via an **app-side AWS SSM Parameter Store configuration provider**
  over a single path prefix (`/jira-sync/...`), authorized by **one** task-role grant — so adding a
  secret is "put a parameter under the prefix," with **no new IAM grant, no task-def change, and no
  redeploy** (this cancels the outage class we hit). **BREAKING** for the deployment's current
  secret mechanism: the Jira token moves off ECS injection and the webhook secret off its plain env
  var into Parameter Store.
- Forbid real secret values in committed `appsettings.json`.
- Keep the provider **swappable per cloud** — AWS SSM today, Azure Key Vault on the eventual Azure
  move — without changing how the application consumes secrets.
- **Fail fast** if a required secret can't be resolved at startup, rather than run misconfigured.

## Capabilities

### New Capabilities
- `secrets-configuration`: how the application resolves secrets across environments — layered
  configuration, no secrets in source, a portable managed-store provider in production, and
  fail-fast on missing required secrets.

### Modified Capabilities
- `aws-deployment`: the "Secrets sourced from AWS" requirement becomes specific — secrets are
  resolved from **SSM Parameter Store** via an app-side provider over a single path prefix with one
  task-role grant, replacing ECS secret injection / plain env vars.

## Impact

- `src/SorryDave.JiraSync.Api` — add the SSM Parameter Store configuration provider in `Program.cs`
  (AWS SDK + `Amazon.Extensions.Configuration.SystemsManager`), guarded to AWS only.
- `src/SorryDave.JiraSync.AppHost` — stop ECS-injecting the Jira token and setting the webhook
  secret env var; instead grant the **task role** `ssm:GetParametersByPath` + `kms:Decrypt` on the
  `/jira-sync/` prefix.
- Operational: create SSM SecureString parameters (`/jira-sync/Jira/ApiToken`,
  `/jira-sync/Webhook/Secret`, and the Slack/Anthropic ones as those land).
- The two Slack changes and any Claude integration **reference this convention** instead of inventing
  their own secret delivery.
- No change to local dev (user-secrets) or to the API's config-key names.
