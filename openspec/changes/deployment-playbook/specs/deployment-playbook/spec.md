## ADDED Requirements

### Requirement: Prerequisites and accounts

The playbook SHALL list every account, credential, external service, and local tool an administrator
needs before starting, so they can gather prerequisites in one pass.

#### Scenario: Administrator gathers prerequisites

- **WHEN** an administrator reads the prerequisites section
- **THEN** it names the required AWS account + credentials, Atlassian/Jira Cloud site, Slack
  workspace, a registered domain with a Route 53 hosted zone, and the local toolchain (.NET SDK,
  Docker, AWS CDK CLI, Aspire CLI), and notes the Aspire/CDK deploy mechanics are covered elsewhere

### Requirement: Jira configuration and permissions

The playbook SHALL document how to obtain the Jira API token, the exact account permissions required,
the configuration values, and how to register the inbound webhook.

#### Scenario: Administrator configures Jira

- **WHEN** an administrator follows the Jira section
- **THEN** they create an API token, learn the required permissions (browse the tracked project, add
  comments for write-back, and that webhook registration needs a Jira admin), set
  `Jira:BaseUrl/Email/ApiToken/ProjectKeys`, and register the webhook at `/webhooks/jira?secret=…`
  for the issue/comment events

#### Scenario: Jira Cloud email limitation is stated

- **WHEN** an administrator reads the Jira section
- **THEN** it notes that Jira Cloud hides user email, so by-email Slack identity resolution is
  unavailable there (config-map / invite-list is used instead)

### Requirement: Slack app configuration and scopes

The playbook SHALL document creating the Slack app, the required bot scopes, obtaining the bot token
and signing secret, and the non-secret Slack configuration.

#### Scenario: Administrator configures Slack

- **WHEN** an administrator follows the Slack section
- **THEN** they create the app from the provided manifest, see the required bot scopes
  (`channels:manage`, `channels:read`, `chat:write`, `pins:write`, `users:read`, `users:read.email`),
  install it to obtain the `xoxb-…` bot token, copy the signing secret, and set the non-secret
  config (`Slack:InviteUserIds`, `Slack:UserMap`, `Slack:EligibleIssueTypes`)

### Requirement: Secret and key provisioning

The playbook SHALL provide a secret inventory and document exactly how each secret is provisioned for
local development and for AWS, consistent with the platform's layered-configuration convention.

#### Scenario: Administrator provisions secrets

- **WHEN** an administrator follows the secrets section
- **THEN** it lists every secret (Jira token, webhook secret, Slack bot token + signing secret;
  Anthropic key noted as future) with its config key, its local home (user-secrets) and its AWS home
  (SSM `/jira-sync/*`), and the exact commands — including which secrets ride the AppHost transport
  versus `aws ssm put-parameter` — with no real secret values shown

### Requirement: Deploy, verify, and TUI setup

The playbook SHALL give the operative deploy step (referencing the IaC, not re-documenting it), the
post-deploy verification checks, and how to configure and launch the smoke-test TUI. The playbook
SHALL state that the **TUI is optional and is never deployed** — it is a local operator tool that is
useful for smoke-testing different areas of the platform (it drives whichever API target is selected,
local or remote).

#### Scenario: Administrator deploys and verifies

- **WHEN** an administrator follows the deploy and TUI sections
- **THEN** they run the deploy step, verify `/health`, the work-item list against real Jira, and the
  webhook secret enforcement (401 unsigned / 200 signed), and configure the TUI targets
  (`local`/`aws`) and the webhook-secret user-secret, then launch it

#### Scenario: TUI is optional

- **WHEN** an administrator reads the TUI section
- **THEN** it states that the TUI is not part of the deployment — it is an optional local tool for
  smoke-testing areas of the platform, and the platform runs fully without it

### Requirement: Administrator smoke-test walkthrough

The playbook SHALL provide an ordered, run-it-yourself smoke-test sequence performed from the TUI,
each step stating the action, the expected result, and where to verify it.

#### Scenario: Administrator runs the smoke test

- **WHEN** an administrator follows the smoke-test walkthrough
- **THEN** each step (backfill/list, simulate webhook, submit write-back, Slack provision/show/
  archive/unarchive, switch to the AWS target) states what to do, the expected outcome, and the
  place to confirm it (in Jira, in Slack, or via the API)

### Requirement: Troubleshooting of known issues

The playbook SHALL include a troubleshooting section covering the known deployment and runtime
gotchas with their fixes.

#### Scenario: Administrator hits a known issue

- **WHEN** an administrator encounters a documented failure mode (e.g. the SSM path-node grant, AZ
  rebalancing vs single-instance, the SQLite writable path, the ALB health-check 200, the deploy CLI
  exiting while CloudFormation succeeds, a provisioned Slack channel not appearing, or Jira Cloud
  email privacy)
- **THEN** the troubleshooting section explains the cause and the fix

### Requirement: Azure deployment is documented

The playbook SHALL document deploying to Azure as an alternative to AWS — the cloud selector, the
Azure prerequisites and resources (Azure Container Apps, Azure Files, Azure Key Vault, managed
HTTPS), how secrets are provisioned for Azure, and how the TUI targets it — with the Azure-specific
differences from the AWS path called out. Where the Azure path depends on the `azure-deployment`
change not yet being implemented, the playbook SHALL state that status clearly.

#### Scenario: Administrator deploys to Azure

- **WHEN** an administrator follows the Azure section
- **THEN** it shows the cloud selector (`--cloud azure`), the Azure prerequisites (`az login`, a
  subscription), the resources created (Container Apps, Azure Files for the SQLite mount, Key Vault),
  how secrets reach Key Vault, the managed HTTPS endpoint, and how to point the TUI at the Azure
  target — noting the parts pending implementation of the `azure-deployment` change
