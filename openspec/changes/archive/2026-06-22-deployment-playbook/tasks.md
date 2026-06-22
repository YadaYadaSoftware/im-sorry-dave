# Tasks

## 1. Scaffold

- [x] 1.1 Create top-level `INSTRUCTIONS.md` with the seven numbered phases and a short intro that
      states the goal (zero-to-verified) and that Aspire/CDK internals are out of scope (referenced)

## 2. Phase 0 — Prerequisites & accounts

- [x] 2.1 List accounts (AWS + credentials, Atlassian/Jira Cloud, Slack workspace), the domain +
      Route 53 zone, and the local toolchain (.NET 10 SDK, Docker, AWS CDK CLI, Aspire CLI)

## 3. Phase 1 — Jira

- [x] 3.1 Create the API token (id.atlassian.com) and the required account permissions (browse the
      tracked project, add comments for write-back); state Jira-admin is needed to register webhooks
- [x] 3.2 Config values (`Jira:BaseUrl/Email/ApiToken/ProjectKeys`) and webhook registration to
      `/webhooks/jira?secret=…` (UI + REST), scoped to the project, with the issue/comment events
- [x] 3.3 Note Jira Cloud hides user email → by-email Slack identity resolution unavailable (use
      config-map / invite-list)

## 4. Phase 2 — Slack

- [x] 4.1 Create the app from `docs/slack-app-manifest.yaml`; document the bot scopes
- [x] 4.2 Install to workspace → Bot User OAuth Token (`xoxb-…`) = `Slack:BotToken`; signing secret
      = `Slack:SigningSecret`; finding the operator's Slack user id (`users.list`)
- [x] 4.3 Non-secret config: `Slack:InviteUserIds`, `Slack:UserMap`, `Slack:EligibleIssueTypes`

## 5. Phase 3 — Secrets & keys

- [x] 5.1 Secret inventory table (config key · local home · AWS home) with no real values
- [x] 5.2 Exact commands: `dotnet user-secrets set` (API + AppHost), `aws ssm put-parameter` for the
      Jira token + webhook secret, and the AppHost transport for Slack secrets; how to generate the
      webhook secret. Flag the split and the unify-under-AppHost follow-up

## 6. Phase 4 — Deploy & verify

- [x] 6.1 The operative deploy step (reference `aspire deploy`, not the IaC internals)
- [x] 6.2 Verification: `/health`, `/workitems` (real Jira), webhook 401 unsigned / 200 signed; note
      the "CLI exits but CloudFormation succeeds" behavior

## 7. Phase 5 — TUI setup

- [x] 7.1 `ApiTargets` (`local`/`aws`), `ActiveApiTarget`, the `ApiTargets:aws:WebhookSecret`
      user-secret, and launching via the AppHost (▶ console) or standalone `--target`

## 8. Phase 6 — Administrator smoke-test walkthrough

- [x] 8.1 Ordered TUI steps as `Action → Expected → Verify where`: backfill/list → simulate webhook
      → submit write-back (verify the Jira comment) → Slack provision (verify channel + pinned
      context + invite in Slack) → show linked channel → archive/unarchive → switch Target to `aws`
      and repeat list/simulate

## 9. Phase 7 — Troubleshooting

- [x] 9.1 Known gotchas + fixes: SSM path-node grant, AZ rebalancing vs single-instance, SQLite
      writable path, ALB health-check 200, deploy CLI exit vs CloudFormation, Slack channel
      visibility (browse/invite), Jira Cloud email privacy

## 9b. Azure deployment

- [x] 9b.1 Document the Azure path: cloud selector (`--cloud azure`), Azure prerequisites
      (`az login`, subscription), resources (ACA, Azure Files, Key Vault), Key Vault secret
      provisioning, managed HTTPS, and the TUI `azure` target — noting the parts pending the
      `azure-deployment` change

## 10. Cross-link & verify

- [x] 10.1 Trim the README "Deploying to AWS" section to an overview and link to `INSTRUCTIONS.md`
- [x] 10.2 Dry-read the smoke-test steps against the running platform; correct any expected-result
      mismatches
