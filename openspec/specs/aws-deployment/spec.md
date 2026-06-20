# aws-deployment Specification

## Purpose
TBD - created by archiving change aws-aspire-deployment. Update Purpose after archive.
## Requirements
### Requirement: Deploy from the AppHost via `aspire deploy`

The Aspire AppHost SHALL be configured so that a single `aspire deploy` invocation, given AWS
credentials, builds the API container image, pushes it to Amazon ECR, and provisions or updates the
running service in AWS.

#### Scenario: One-command deploy

- **WHEN** an operator runs `aspire deploy` with valid AWS credentials configured
- **THEN** the API container image is built, pushed to ECR, and the AWS compute service is created or updated to run it

#### Scenario: Missing AWS credentials

- **WHEN** `aspire deploy` is run without resolvable AWS credentials
- **THEN** the deploy fails with a clear error and provisions nothing

### Requirement: Public HTTPS endpoint

The deployed API SHALL be reachable over HTTPS at a stable public URL, and that URL SHALL be
reported after a successful deploy.

#### Scenario: Endpoint reachable over HTTPS

- **WHEN** the deploy completes
- **THEN** the API answers `GET /health` over HTTPS at the reported public URL

#### Scenario: Public URL surfaced for webhook registration

- **WHEN** a deploy completes
- **THEN** the operator is given the public base URL to register Jira's webhook (and later the Slack app) against

### Requirement: Only the API is deployed

The deployment SHALL include the API service and SHALL exclude the interactive console/TUI, which
remains a local development tool.

#### Scenario: Console excluded from the deployable artifacts

- **WHEN** the AppHost publishes/deploys to AWS
- **THEN** the API is containerized and deployed, and the interactive console resource is not deployed

### Requirement: Durable persistence in AWS

The deployed service SHALL persist its data (work items, mappings, write-back outbox) across
container restarts and redeploys; an ephemeral container filesystem SHALL NOT be the data store.

#### Scenario: Data survives a restart

- **WHEN** the deployed service is restarted or redeployed
- **THEN** previously mirrored work items and outbox records are still present

### Requirement: Secrets sourced from AWS

The deployed service SHALL resolve its secrets (Jira API token, webhook secret, Slack bot token and
signing secret, Anthropic API key) at runtime from **AWS SSM Parameter Store** via an app-side
configuration provider over a single path prefix (`/jira-sync/`), authorized by a single task-role
grant on that prefix (plus `kms:Decrypt` for SecureString). Secrets SHALL NOT be baked into the
container image, the task definition, or source, and adding a new secret SHALL NOT require a new IAM
grant or a redeploy.

#### Scenario: Secrets resolved from Parameter Store at startup

- **WHEN** the service starts in AWS
- **THEN** it loads its secrets from SSM Parameter Store under the `/jira-sync/` prefix, with none present in the image, task definition, or repository

#### Scenario: New secret needs no new grant or redeploy

- **WHEN** a new SecureString parameter is added under `/jira-sync/`
- **THEN** the service resolves it on its next start using the existing task-role grant, with no new IAM grant and no change to the task definition

### Requirement: Single-instance run for background workers

The deployment SHALL run a single instance in this version so the reconciliation and write-back
background workers retain their single-writer assumption.

#### Scenario: Single instance enforced

- **WHEN** the service is deployed
- **THEN** exactly one instance runs, so the outbox is drained by a single writer (no concurrent senders)

