## Context

Every secret today is already an `IConfiguration` key (`Jira:ApiToken`, `Webhook:Secret`); the API
reads `builder.Configuration[...]` and is oblivious to the source. So the convention is not about the
API — it is about **which provider populates the key in each environment**. The current state is
ad-hoc: the Jira token is ECS-injected from Secrets Manager (execution-role grant), the webhook
secret is a plain env var. Adding a *new* ECS-injected secret in a stop-then-start, single-instance
deploy outaged the service because the new task launched before its `GetSecretValue` grant
propagated and there was no old task to fall back on. Three more secrets (Slack bot token, Slack
signing secret, Anthropic key) are imminent, so the delivery mechanism needs to scale without that
failure mode.

## Goals / Non-Goals

**Goals:**
- One convention: secrets are config keys; the API stays source-agnostic.
- A defined precedence so a secret can be supplied or overridden per environment.
- Production secret delivery where **adding a secret needs no new grant, no task-def change, no
  redeploy**.
- No real secrets in committed `appsettings.json`.
- A provider that is swappable per cloud (AWS now, Azure later).
- Fail fast when a required secret is missing.

**Non-Goals:**
- Managed secret *rotation* (these are static tokens; revisit if a rotating credential appears).
- Changing the API's config-key names or the local user-secrets workflow.
- Encrypting anything in transit beyond what the store/TLS already provide.

## Decisions

### Secrets are layered configuration keys

**Decision:** Keep every secret a plain config key and stack the providers with a defined winner
(last wins):

```
  appsettings.json   →   SSM provider (prod)   →   environment variables   →   user-secrets (dev)
  structure +            real secrets               override / quick set        local real secrets
  NON-secret defaults    under /jira-sync/
```

Any secret can come from the store *or* an env var; the env var wins, giving operational
flexibility without code changes. *Why:* the API already works this way; we only choose providers.

### Production delivery is an app-side SSM provider, not ECS injection

**Decision:** In AWS the service loads secrets itself at startup via the **SSM Parameter Store
configuration provider** over the `/jira-sync/` path prefix — *not* ECS task-definition secret
injection.

| | ECS injection (rejected) | App-side SSM provider (chosen) |
|---|---|---|
| App sees | env var | config key |
| IAM grant on | execution role | **task** role |
| Add a new secret | new task-def `secrets` entry **+ new grant** → stop-then-start **outage risk** | **put a parameter under the prefix** — no grant, no task-def change, no redeploy |
| In task definition | ARN reference | nothing |

*Why:* the task role gets **one** grant on the prefix (`ssm:GetParametersByPath` + `kms:Decrypt`),
already propagated; every future secret is just another parameter under it. This directly cancels the
grant-propagation outage we hit. *Alternative — keep ECS injection:* rejected; it reintroduces the
per-secret grant and the outage each time we add one.

### SSM Parameter Store over Secrets Manager

**Decision:** Use **SSM Parameter Store (SecureString)**, not Secrets Manager. Parameter paths map
1:1 to config keys (`/jira-sync/Slack/BotToken` → `Slack:BotToken`), there is a **first-party** .NET
provider (`builder.Configuration.AddSystemsManager("/jira-sync")`), it is ~free, and SecureString is
KMS-encrypted. *Why not Secrets Manager:* its headline feature is managed rotation, which none of
these static tokens need, and it costs per secret. *Trade-off accepted:* if a rotating credential
(e.g. a database password) ever appears, that *one* secret can live in Secrets Manager alongside the
SSM ones — the layered model allows mixing.

### One grant, fail fast, restart to rotate

**Decision:**
- **Authorization:** a single task-role policy on `arn:.../parameter/jira-sync/*` plus `kms:Decrypt`
  on the SecureString key. No per-secret grants.
- **Startup failure:** the SSM provider is **required** in AWS; if it cannot resolve, the service
  fails to start (ECS restarts it) rather than running without credentials. *Why:* a service silently
  up with no Jira token is worse than a visible crash-loop.
- **Rotation:** read-at-startup; changing a parameter takes effect on the next task start (force a
  new deployment to roll it). A provider **reload interval** is *deferred* — easy to add later, not
  needed for static tokens.

### Portable by provider swap

**Decision:** Only the provider line is cloud-specific. On the Azure move, replace
`AddSystemsManager("/jira-sync")` with the **Azure Key Vault** configuration provider; the config
keys, the API code, and the precedence model are unchanged. *Why:* keeps the "eventually Azure"
direction cheap — the *pattern* is portable even though the provider isn't.

### Migrate the two existing secrets onto the convention

**Decision:** Move `Jira:ApiToken` (currently ECS-injected) and `Webhook:Secret` (currently a plain
env var) into Parameter Store under `/jira-sync/`, and remove their bespoke wiring from the AppHost.
*Why:* one mechanism, and it retires the ECS-injection grant entirely.

## Risks / Trade-offs

- [API gains an AWS-SDK startup dependency] → accepted; isolated to one provider registration,
  guarded to AWS, and replaced wholesale on Azure. The *pattern* stays portable.
- [SSM unreachable at startup] → fail fast + ECS restart; transient AWS issues self-heal on retry.
  Net availability is better than running uncredentialed.
- [Parameter-name drift vs config keys] → the path→key mapping is mechanical (`/` ↔ `:`); document
  the prefix and naming so Slack/Claude parameters are added consistently.
- [Env-var override hides a stale store value] → intentional (override wins); call it out in the
  runbook so an operator knows env beats the store.
- [KMS permissions forgotten] → SecureString needs `kms:Decrypt`; include it in the same task-role
  policy as the SSM grant so they are never split.

## Migration Plan

1. Add the SSM Parameter Store provider to `Program.cs`, registered only when running in AWS, over
   the `/jira-sync/` prefix; keep env/user-secrets layers as-is.
2. Create SecureString parameters `/jira-sync/Jira/ApiToken` and `/jira-sync/Webhook/Secret`.
3. Grant the **task role** `ssm:GetParametersByPath` on `/jira-sync/*` + `kms:Decrypt`; remove the
   Jira-token ECS secret and the webhook-secret env var from the AppHost.
4. Deploy and verify (real MDP still mirrors; webhook still returns 401 unsigned / 200 signed).
5. Document the convention + parameter naming; Slack/Claude add their parameters under the prefix
   when those changes implement.
- *Rollback:* re-add the env/ECS wiring in the AppHost; the layered model means a plain env var still
  overrides, so reverting is a config change, not a code rewrite.

## Open Questions

- None blocking. Deferred: a provider **reload interval** for hot rotation, and whether a future
  rotating credential warrants a single Secrets Manager entry alongside the SSM parameters.
