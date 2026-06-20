## Context

The smoke TUI is a thin **client of the running API** (`AppServices.Build` constructs one
`HttpClient` from a single resolved base URL; `ApiClient` wraps it; `MainWindow` shows the URL in
the status bar). Today the base URL comes from `services:api:http:0` (Aspire), then `ApiBaseUrl`,
then a localhost default. There are now two live backends an operator wants to drive from the same
console — the **local debugging API** and the **deployed AWS API** — and the AWS API **secures
`POST /webhooks/jira`** (the TUI's "Simulate webhook" path), so a base URL alone is no longer
enough: the AWS target also needs the webhook shared secret. This change makes the set of targets
first-class, selectable, and self-contained.

## Goals / Non-Goals

**Goals:**
- Carry connection details for **multiple named API targets** (local + AWS out of the box), each
  with everything needed to use it (base URL, and webhook secret where required).
- Choose the **active target** at startup (default/CLI) and **switch at runtime** from the TUI.
- Make "Simulate webhook" work against the **secured** AWS endpoint by sending the target's secret.
- Preserve **Aspire** launch behavior (AppHost-injected endpoint still honored).
- Keep secrets out of the repo (user-secrets / environment).

**Non-Goals:**
- Any server-side change. The deployed API and its webhook secret are untouched.
- Auth beyond the webhook secret (the other endpoints need none today).
- Persisting the operator's last choice across runs, or editing targets from inside the TUI.

## Decisions

### Targets are a named dictionary in configuration

**Decision:** Define targets as a config dictionary keyed by name, each a small `ApiTarget`
(`BaseUrl`, optional `WebhookSecret`), plus an `ActiveApiTarget` selecting the default:

```jsonc
// appsettings.json (non-secret)
"ApiTargets": {
  "local": { "BaseUrl": "http://localhost:5050" },
  "aws":   { "BaseUrl": "https://jsg.appcloud.systems" }
},
"ActiveApiTarget": "local"
```
```bash
# user-secrets (the AWS webhook secret — never committed)
dotnet user-secrets set "ApiTargets:aws:WebhookSecret" "<value>" --project src/SorryDave.JiraSync.SmokeTui
```

*Why a dictionary keyed by name:* user-secrets and environment overrides read naturally
(`ApiTargets:aws:WebhookSecret`), the name is the menu label, and binding is a plain
`Dictionary<string, ApiTarget>`. *Alternative — an array of objects* (`ApiTargets:0:...`): rejected,
index-based secret keys are awkward and reorder-fragile.

### Aspire injection becomes an implicit target, and wins when present

**Decision:** When `services:api:http:0` / `https:0` is present (i.e. launched by the AppHost), add
an implicit **`aspire`** target from it and make it the active one unless `ActiveApiTarget` is set
explicitly. *Why:* preserves today's behavior (AppHost-launched console talks to the AppHost's API)
while standalone runs fall back to the configured default (`local`). *Alternative — ignore Aspire
once targets exist:* rejected, it would break the dashboard ▶ launch flow.

### Selection: default + CLI at startup, menu at runtime

**Decision:** The active target is `ActiveApiTarget` (overridable on the command line, e.g.
`--target aws`, mapped to `ActiveApiTarget`). At runtime a **"Target" menu** lists the configured
targets; choosing one **rebuilds the `ApiClient`** against the new base URL + secret and refreshes
the active panel. *Why:* covers both "launch straight into the right backend" and "flip between them
mid-session" without restart. *Alternative — startup-only:* rejected, the whole point is comparing
backends live. *Alternative — a free-form URL prompt:* rejected, targets are pre-configured so the
secret travels with the URL and the operator can't fat-finger an unsecured AWS call.

### The webhook secret is applied only to the secured call

**Decision:** `ApiClient` is constructed with the active target's optional `WebhookSecret`; it
appends `?secret=<value>` to `POST /webhooks/jira` when set, and sends nothing extra when not.
*Why:* that endpoint is the only secured one; the AWS API rejects unsigned webhooks with 401, local
(no secret) accepts either. Query param matches how the registered Jira webhook authenticates.
*Alternative — `X-Webhook-Secret` header:* equivalent; query param keeps it consistent with the
registered webhook URL and is simplest to verify. The secret is never logged or shown in the UI.

### Status bar shows the active target; errors stay non-fatal

**Decision:** The status bar shows `Target: <name> (<url>)` for the active target. A missing/empty
target list falls back to the localhost default so the console still launches. Connection failures
remain in-UI (existing "Result and error visibility" requirement), so pointing at an unreachable
target never crashes the console.

## Risks / Trade-offs

- [Secret leakage] → webhook secret lives in user-secrets/env, is applied only to the webhook call,
  and is never rendered in the status bar, logs, or error text.
- [Switching mid-action] → rebuild the client between actions and refresh; in-flight requests use
  the client they started with. The single-panel TUI makes this low-risk.
- [Stale/unsecured AWS calls] → because the secret is bound to the target, selecting `aws` always
  carries its secret; an operator can't accidentally hit AWS unsigned.
- [Aspire vs. explicit default ambiguity] → explicit `ActiveApiTarget`/`--target` always wins; the
  implicit `aspire` target only fills the gap when nothing is chosen.

## Migration Plan

1. Add the `ApiTarget` model + `ApiTargets`/`ActiveApiTarget` config (local + aws in appsettings).
2. Rework `AppServices.Build` to return the resolved target set + active target; thread the webhook
   secret into `ApiClient`.
3. Add the runtime "Target" menu + status-bar label in `MainWindow`; rebuild the client on switch.
4. Set the AWS webhook secret in the TUI's user-secrets; verify both targets (list work items on
   each; "Simulate webhook" succeeds on AWS).
- *Rollback:* with a single configured target (or none), behavior is identical to today — the menu
  just has one entry and the localhost default still applies.

## Open Questions

- None outstanding for this change.
