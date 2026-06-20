# Tasks

## 1. App-side SSM configuration provider

- [ ] 1.1 Add `Amazon.Extensions.Configuration.SystemsManager` to the API project
- [ ] 1.2 In `Program.cs`, register `builder.Configuration.AddSystemsManager("/jira-sync")` **only when running in AWS** (e.g. gated on an env flag or detected execution context), layered below environment variables so env vars still override
- [ ] 1.3 Confirm the path→key mapping (`/jira-sync/Jira/ApiToken` → `Jira:ApiToken`, etc.) resolves the existing config keys unchanged

## 2. Fail-fast on required secrets

- [ ] 2.1 Validate required secrets at startup (at minimum `Jira:ApiToken` when not using the fake backend); throw/exit if unresolved so ECS restarts rather than running uncredentialed
- [ ] 2.2 Ensure the failure surfaces a clear log line (which key is missing) without printing the value

## 3. Provision Parameter Store

- [ ] 3.1 Create SecureString parameters `/jira-sync/Jira/ApiToken` and `/jira-sync/Webhook/Secret` (values from the existing Secrets Manager entries)
- [ ] 3.2 Document the naming convention so Slack/Claude parameters (`/jira-sync/Slack/BotToken`, `/jira-sync/Slack/SigningSecret`, `/jira-sync/Anthropic/ApiKey`) are added consistently

## 4. AppHost: grant the task role, retire old wiring

- [ ] 4.1 Grant the **task role** `ssm:GetParametersByPath` on `arn:.../parameter/jira-sync/*` + `kms:Decrypt` on the SecureString key (single grant)
- [ ] 4.2 Remove the Jira-token ECS secret injection and the `Webhook__Secret` env var from `AppHost.cs`
- [ ] 4.3 Keep plain env vars working as a higher-precedence override (no code needed beyond config layering — verify)

## 5. appsettings hygiene

- [ ] 5.1 Confirm `appsettings.json` holds only non-secret structure/defaults; no real secret values committed

## 6. Verify (deploy)

- [ ] 6.1 Deploy and confirm the service resolves secrets from Parameter Store: real MDP still mirrors; `/health` Healthy
- [ ] 6.2 Confirm the webhook is still secured (401 unsigned, 200 with `?secret=`) with the secret now sourced from SSM
- [ ] 6.3 Add a throwaway parameter under `/jira-sync/` and confirm it resolves on next task start with **no** new IAM grant or task-def change; then remove it

## 7. Docs & propagation

- [ ] 7.1 Document the secrets convention (layered precedence, SSM prefix, env override, fail-fast, Azure Key Vault swap) in the README deployment section
- [ ] 7.2 Note in the Slack and Claude/summarization changes that their secrets follow this convention (parameters under `/jira-sync/`), not bespoke delivery
