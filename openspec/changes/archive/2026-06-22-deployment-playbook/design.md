## Context

The platform now spans Jira, Slack, AWS (ECS/ALB/EFS/SSM), and a smoke-test TUI, with several
secrets and external permissions. The Aspire/CDK deployment is IaC, but a new administrator still
has to obtain accounts and API keys, grant the right Jira/Slack permissions, provision secrets into
the right places, set up the TUI, and confirm the result. That knowledge exists only as a short
README runbook plus build-time tribal knowledge. This change adds the missing front-to-back,
manual-verification playbook. It is documentation only — it reads the system as built and writes it
down accurately.

## Goals / Non-Goals

**Goals:**
- A single `INSTRUCTIONS.md` a new admin follows top-to-bottom to reach a **verified** deployment.
- **Accurate** to the implementation: real config keys, SSM paths, Slack scopes, endpoints.
- Manual **smoke-test walkthrough** from the TUI, with expected results and where to verify each.

**Non-Goals:**
- Re-documenting the Aspire/CDK IaC internals (referenced as a step).
- Automating provisioning or CI/CD (this is a human playbook).
- Changing any code or the deployment mechanics.

## Decisions

### One top-level `INSTRUCTIONS.md`; the README stays the overview
**Decision:** Put the full playbook in repo-root `INSTRUCTIONS.md`; the README keeps its short
"Deploying to AWS" overview and **links** to `INSTRUCTIONS.md` for the complete guide. *Why:* one
authoritative deep guide, no duplicated step lists drifting apart. *Alternative:* expand the README
in place — rejected (it becomes unwieldy and mixes overview with a long procedure).

### Linear "zero-to-verified" structure
**Decision:** Numbered phases in dependency order: **0. Prerequisites & accounts → 1. Jira → 2.
Slack → 3. Secrets & keys → 4. Deploy & verify → 5. TUI setup → 6. Administrator smoke test → 7.
Troubleshooting.** Secrets come after Jira/Slack because their values are produced there. *Why:* an
admin can execute it once, in order, without forward references.

### State permissions explicitly, not "be an admin"
**Decision:** Name the concrete permissions/scopes:
- **Jira:** an **API token** (the service acts as that user); the account needs **Browse Projects**
  and **Add Comments** on the tracked project (write-back); **webhook registration** needs a Jira
  **admin** (System → WebHooks, or the `/rest/webhooks/1.0/webhook` REST API). Note Jira **Cloud
  hides user email** (so by-email Slack identity resolution doesn't work there — config-map/invite
  list instead).
- **Slack:** create the app from `docs/slack-app-manifest.yaml`; **bot scopes** `channels:manage`,
  `channels:read`, `chat:write`, `pins:write`, `users:read`, `users:read.email`; **Install to
  Workspace** to mint the **Bot User OAuth Token** (`xoxb-…`); **Signing Secret** from Basic
  Information. Org-restricted workspaces may need a workspace admin to approve the install.

### Document secret provisioning as-built, and flag the split
**Decision:** The **secret inventory** with each item's source of truth and how to set it:

| Config key | Local (debug) | AWS (prod) |
|---|---|---|
| `Jira:ApiToken` | API project user-secrets | SSM `/jira-sync/Jira/ApiToken` (`put-parameter`) |
| `Webhook:Secret` | API project user-secrets | SSM `/jira-sync/Webhook/Secret` (`put-parameter`) |
| `Slack:BotToken` | API + AppHost user-secrets | SSM `/jira-sync/Slack/BotToken` (AppHost transport on deploy) |
| `Slack:SigningSecret` | API + AppHost user-secrets | SSM `/jira-sync/Slack/SigningSecret` (AppHost transport) |
| `Anthropic:ApiKey` *(future)* | user-secrets | SSM `/jira-sync/Anthropic/ApiKey` |

Non-secret config (`Jira:BaseUrl/Email/ProjectKeys`, `Slack:InviteUserIds/UserMap/EligibleIssueTypes`,
`ApiTargets`) lives in committed `appsettings.json`. *Why document the split:* Slack secrets ride
the AppHost user-secrets→SSM transport, while the Jira token and webhook secret are
`put-parameter`'d directly — the playbook states both clearly and notes **unifying everything under
the AppHost transport** as a possible follow-up so an admin sets all secrets in one place.

### Smoke test = ordered checklist with expected result + verification site
**Decision:** Each TUI step is `Action → Expected → Verify where`. The sequence: launch AppHost →
▶ console → Backfill/list (real MDP) → select item → Simulate webhook → Submit write-back (verify
the comment in Jira) → Slack ▸ Provision (verify the channel + pinned context + your invite in
Slack) → Show linked channel → Archive/Unarchive → switch **Target → aws** and repeat list/simulate
against the live instance. *Why:* the admin proves each subsystem (Jira read, write-back, Slack
provisioning/lifecycle, target switching) with a concrete observable.

## Risks / Trade-offs

- [Doc drift as config evolves] → keep keys/paths/scopes named exactly as in code; cross-link from
  the README; the secret-inventory table is the most drift-prone part — anchor it to the SSM paths.
- [Leaking real secrets into the doc] → placeholders only (`xoxb-…`, `<token>`); never paste live
  values; reference `docs/slackcredentials.md` is gitignored and to be deleted.
- [Overlap with the README runbook] → README shrinks to an overview + a link; the playbook is the
  single source for the procedure.

## Migration Plan

1. Write `INSTRUCTIONS.md` with the seven phases above, drawn from the as-built system.
2. Trim the README "Deploying to AWS" section to an overview and link to `INSTRUCTIONS.md`.
3. Dry-read the smoke-test steps against the running platform to confirm expected results match.

## Open Questions

- None blocking. Deferred: whether to unify all secret provisioning under the AppHost transport
  (a code change) — noted in the playbook as a follow-up, not done here.
