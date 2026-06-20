## MODIFIED Requirements

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
