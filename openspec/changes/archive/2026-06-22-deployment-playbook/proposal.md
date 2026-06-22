## Why

A new administrator standing up the platform needs a single, accurate, **zero-to-verified**
playbook. The Aspire/CDK side is already IaC, but everything *around* it — which accounts and API
keys to obtain, the exact Jira and Slack permissions/scopes, how the secrets are provisioned and
where they live, how to set up the smoke-test TUI, and how to confirm a working deployment — is
scattered across the README runbook and tribal knowledge from the build. There is no step-by-step
guide an admin can follow front-to-back, including manual TUI verification as they go.

## What Changes

- Add a top-level **`INSTRUCTIONS.md`** — a complete administrator deployment playbook:
  - **Prerequisites & accounts** — AWS (with credentials), an Atlassian/Jira Cloud site, a Slack
    workspace, a registered domain + Route 53 zone, and the local toolchain.
  - **Jira setup** — create the API token, the account permissions required (read tracked project
    issues, add comments for write-back, the limits of Cloud user-email visibility), the config
    values (`Jira:BaseUrl/Email/ApiToken/ProjectKeys`), and **webhook registration** to
    `/webhooks/jira?secret=…`.
  - **Slack setup** — create the app from `docs/slack-app-manifest.yaml`, the **bot scopes**
    (`channels:manage`, `channels:read`, `chat:write`, `pins:write`, `users:read`,
    `users:read.email`), obtaining the **bot token** (`xoxb-…`) and **signing secret**, and the
    non-secret config (`Slack:InviteUserIds`, `Slack:UserMap`, `Slack:EligibleIssueTypes`).
  - **Secrets & keys** — the layered config convention, the **secret inventory** (Jira token,
    webhook secret, Slack bot token + signing secret; Anthropic key noted as future), and exactly
    how each is provisioned (SSM `/jira-sync/*`, the AppHost transport, local user-secrets).
  - **Deploy & verify** — the operative steps (referencing `aspire deploy`, not re-documenting the
    IaC) and the health/work-item/webhook checks.
  - **TUI setup** — targets (`local`/`aws`), the webhook-secret user-secret, and how to launch.
  - **Administrator smoke-test walkthrough** — an ordered, run-it-yourself sequence in the TUI
    (backfill → simulate webhook → write-back → Slack provision/show/archive → switch to AWS),
    each with the **expected result** and how to verify it (in Jira, in Slack, in the API).
  - **Troubleshooting** — the known gotchas (SSM path-node grant, AZ rebalancing, SQLite writable
    path, ALB health 200, deploy-CLI-exits-but-CloudFormation-succeeds, Slack channel visibility,
    Jira Cloud email privacy).
- **Out of scope:** re-documenting Aspire/CDK internals (already IaC) — referenced as a step.
- Cross-link from the README so the README stays the overview and `INSTRUCTIONS.md` is the
  authoritative deep guide.

## Capabilities

### New Capabilities
- `deployment-playbook`: a complete, accurate administrator deployment guide covering accounts,
  Jira/Slack permissions & keys, secret provisioning, TUI setup, and a manual smoke-test
  walkthrough — sufficient for a new admin to deploy and verify the platform from scratch.

## Impact

- **Docs only:** new `INSTRUCTIONS.md` at the repo root + a README cross-link. No code changes.
- Pulls together facts from `secrets-configuration-convention`, `aws-aspire-deployment`,
  `slack-channel-provisioning`, and `jira-sync-core`.
- Surfaces (and documents, not yet fixes) the secret-provisioning split — Slack secrets ride the
  AppHost transport while the Jira token / webhook secret are SSM `put-parameter`'d — and notes
  unifying them as a possible follow-up.
