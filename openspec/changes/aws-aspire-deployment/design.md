## Context

The platform must be publicly reachable over HTTPS for Jira webhooks (and, next, Slack
Events/commands). We already have an Aspire AppHost orchestrating the API; we want to reuse it to
deploy the API as a container to AWS via `aspire deploy`, with credentials we supply. The
interactive console/TUI is a dev tool and is not deployed.

**AWS is the interim target; Azure is the eventual home.** Aspire's most mature deploy path is
Azure (via `azd`/Azure publishers), so if the AWS path causes big trouble we can switch to Azure
as a sanctioned fallback. That makes **cloud-portability** a design priority: keep the deployment
to a provider-agnostic container plus PostgreSQL so moving to **Azure Container Apps + Azure
Database for PostgreSQL** is low-friction.

## Goals / Non-Goals

**Goals:**
- `aspire deploy` from the AppHost builds + pushes the API image and runs it on AWS with public HTTPS.
- Durable data, AWS-sourced secrets, single-instance, console excluded.
- A stable public base URL to point Jira (and later Slack) at.

**Non-Goals:**
- Multi-region/HA, autoscaling, blue-green (single instance for v1).
- Deploying the interactive console.
- CI/CD pipeline automation (manual `aspire deploy` is the v1 mechanism).

## Decisions

- **Aspire AppHost is the deploy driver, AWS via `Aspire.Hosting.AWS`.** Add the AWS hosting
  integration to the AppHost and target AWS so `aspire deploy` orchestrates build → ECR push →
  compute update. *Honesty note:* Aspire's most mature deploy path is Azure (`azd`); the AWS path
  goes through `Aspire.Hosting.AWS` + the AWS toolchain (ECR + CDK/App Runner/ECS). If `aspire
  deploy` to AWS proves immature for our compute target, the near fallback is the same image
  published to ECR and deployed via the AWS CDK/CLI the integration generates; the broader fallback
  is to deploy to **Azure** (Aspire's most mature target) since that's the eventual home anyway. The
  `aspire deploy` UX stays the goal. This is the main risk to validate early (see Open Questions).

- **Compute + persistence are coupled — pick one pairing:**
  - **(A, recommended) AWS App Runner + Amazon RDS for PostgreSQL.** App Runner takes a container
    from ECR and gives managed HTTPS + a stable URL with the least infra. But App Runner has **no
    persistent volume**, so SQLite is not viable there — it forces a managed DB. We move the EF
    Core provider from SQLite to **Npgsql (PostgreSQL)** (the EF model is provider-agnostic; this
    also lets us drop the SQLite-specific in-memory `DateTimeOffset` query workarounds). Cleanest
    managed result; durable; scale-ready later.
  - **(B) ECS Fargate + EFS-mounted SQLite.** Keeps SQLite (no code change) by mounting EFS for the
    DB file, but needs more infra: ALB + ACM certificate for HTTPS, EFS, ECS service/task defs.
    More moving parts; SQLite still caps us at one instance.
  - **Lean:** App Runner + RDS Postgres for a clean managed service — and, given Azure is the
    eventual home, the **portable** choice: a plain container + PostgreSQL maps directly onto Azure
    Container Apps + Azure Database for PostgreSQL, whereas ECS+EFS+SQLite is AWS-locked. ECS+EFS+
    SQLite remains the no-code-change interim if we want to defer the provider swap. Decided with
    the user.

- **Registry: Amazon ECR.** Standard target for the built image.

- **Single instance in v1.** The reconciliation sweep and write-back outbox sender are
  single-writer: two instances draining the same outbox could double-post a comment before either
  marks it Sent (idempotency dedups at submit, not at send). So pin to one instance (App Runner
  min=max=1, or ECS desiredCount=1). Scale-out later needs a lock/leader-election or moving the
  outbox to a queue.

- **Secrets via AWS Secrets Manager.** Jira token, Slack bot token, and signing secret live in
  Secrets Manager and are injected as configuration/environment at runtime. Nothing secret in the
  image or repo. Local dev keeps using user-secrets.

- **Console excluded from deploy.** The console resource is already `WithExplicitStart`; for publish
  it is marked so the AWS deployer does not containerize/deploy it. Only the API ships.

## Risks / Trade-offs

- [`aspire deploy` AWS maturity] → Validate a minimal deploy first; fall back to ECR + CDK/CLI from the integration while keeping the `aspire deploy` goal.
- [SQLite in a container] → Not durable on App Runner; addressed by RDS (option A) or EFS (option B).
- [Provider swap to Npgsql (option A)] → EF model is provider-agnostic, but touches connection setup, migrations, and the SQLite-only query workarounds; needs a Postgres migration set and a test pass.
- [Concurrent writers if scaled] → Pin to single instance; document the constraint.
- [Secret handling] → Secrets Manager + IAM; never in image; plan rotation.
- [Cost] → ECR + App Runner/ECS + RDS/EFS incur ongoing cost; size minimally for v1.

## Migration Plan

1. Add `Aspire.Hosting.AWS` to the AppHost; configure the AWS target (region, profile/credentials) and ECR.
2. Make the API container-publishable; mark the console resource excluded from publish.
3. Apply the chosen persistence: (A) add Npgsql provider + Postgres migrations + RDS connection from Secrets Manager, or (B) wire EFS-mounted SQLite on ECS.
4. Move secrets to AWS Secrets Manager; map them to the API's configuration.
5. Run `aspire deploy` with AWS credentials; confirm `GET /health` over the public HTTPS URL.
6. Register the Jira webhook against the public URL; verify a live event round-trips. (Slack wiring follows in its own changes.)
- *Rollback:* tear down the AWS resources the deploy created; local dev is unaffected.

## Open Questions

- **Compute/persistence pairing:** App Runner + RDS Postgres (recommended, needs Npgsql swap) vs. ECS Fargate + EFS + SQLite (no code change, more infra)?
- AWS **region**, and a **custom domain** (Route53 + ACM) vs. the default App Runner/ALB hostname?
- How mature is `aspire deploy` for our AWS compute target today — does it need the AWS CDK/toolkit underneath? (If it's too rough, switch to the Azure target, which is the eventual home.)
- Credentials at deploy: long-lived access key/secret vs. an assumed role/profile.
- Timing of the eventual Azure move — deploy AWS now and port later, or go straight to Azure if the AWS path stalls?
