## Why

The platform runs locally today (Aspire AppHost, SQLite, no public ingress). To go live it needs
a **public HTTPS endpoint** so Jira webhooks — and, next, Slack Events/slash commands — can reach
it. We want to deploy the existing API as a container to **AWS** and drive that deployment from the
existing Aspire AppHost with **`aspire deploy`**, using AWS credentials we supply.

## What Changes

- Add AWS deployment support to the Aspire AppHost (via the `Aspire.Hosting.AWS` integration) so
  `aspire deploy` builds the API container, pushes it to **Amazon ECR**, and runs it on AWS with a
  managed **public HTTPS** endpoint.
- Package the **API** as a deployable container; **exclude the interactive console/TUI** from the
  deployment (it stays a local dev tool).
- Provide **durable persistence** in AWS (the local SQLite file is not viable in a container) —
  either a managed database or a mounted volume, decided in design.
- Source all **secrets from AWS** (Jira API token, Slack bot token + signing secret) rather than
  baking them into the image.
- After deploy, surface the **public base URL** and document wiring Jira's webhook (and later the
  Slack app's Event Subscriptions + slash command) to it.
- Run as a **single instance** in v1 so the outbox/reconciliation background workers keep their
  single-writer assumption.

## Capabilities

### New Capabilities
- `aws-deployment`: Deploy the API container to AWS from the Aspire AppHost via `aspire deploy`,
  exposing a public HTTPS endpoint with durable persistence and AWS-sourced secrets, excluding the
  interactive console.

### Modified Capabilities
<!-- None. Builds on the existing aspire-orchestration AppHost and jira-sync-core API. -->

## Impact

- New dependency: `Aspire.Hosting.AWS` (13.x) and the AWS toolchain its deploy publisher uses — the
  **AWS CDK CLI** (`aspire publish` emits CDK; `aspire deploy` runs `cdk deploy`). Deploy target is
  **ECS Fargate** (App Runner is not supported). The deploy APIs are experimental.
- New AWS account footprint: ECR repository, an **ECS Fargate** service behind an **ALB + ACM
  certificate** (custom domain) for public HTTPS, a secrets store (AWS Secrets Manager), and a
  database — **Amazon RDS for PostgreSQL** (recommended) or an EFS volume for SQLite. RDS/EFS aren't
  auto-provisioned by the integration, so they're added via CDK.
- Possible code change: if we choose a managed database, the EF Core provider moves from SQLite to
  PostgreSQL (Npgsql) — the EF model is provider-agnostic, but this touches the connection setup and
  the SQLite-specific in-memory query workarounds in `jira-sync-core`.
- Requires AWS credentials (access key + secret, or a profile/role) at deploy time. Incurs AWS cost.
- The deployed endpoint is what Jira and Slack will target; this unblocks the paused Slack changes.
- AWS is the interim target; **Azure is the eventual home**. Keeping the deployment to a portable container + PostgreSQL means moving to Azure Container Apps + Azure Database for PostgreSQL later (or as a fallback if the AWS `aspire deploy` path stalls) is low-friction.
