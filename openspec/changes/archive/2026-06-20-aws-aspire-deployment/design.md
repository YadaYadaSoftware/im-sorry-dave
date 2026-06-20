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

- **`aspire deploy` → AWS CDK → ECS Fargate (verified).** `Aspire.Hosting.AWS` (13.x) added
  publish/deploy support: `aspire publish` transforms the app into AWS CDK constructs synthesized to
  `cdk.out`; `aspire deploy` runs publish then shells out to `cdk deploy`. So `aspire deploy` is the
  real mechanism (it pushes the image to ECR and provisions ECS via CloudFormation). Web projects
  deploy to **ECS Fargate** — an "Express Service" by default, or **ECS Fargate behind an
  Application Load Balancer**. **AWS App Runner is NOT a supported target** (our earlier App-Runner
  plan is dropped). The deploy APIs are **experimental**, gated behind
  `#pragma warning disable ASPIREAWSPUBLISHERS001`. *Risk/fallback:* preview-grade feature — if it
  blocks us, the eventual-Azure target (Aspire's most mature deploy path) is the sanctioned fallback.

- **Public HTTPS via ALB + ACM, in `us-east-1` (decided).** Webhooks need HTTPS. We run **ECS
  Fargate behind an ALB terminating TLS with an ACM certificate** on a **domain we control**, in
  region **`us-east-1`**. ACM DNS-validation records and an ALB alias are wired for that domain
  (cleanest if the domain is in Route 53; external DNS works too with manual records). *Rejected
  alternative:* CloudFront `*.cloudfront.net` (no-domain fallback) — not needed since a domain is
  available.

- **Persistence: EFS-mounted SQLite (decided).** `Aspire.Hosting.AWS` does not provision storage
  out of the box, so we add an **EFS file system via CDK** and mount it on the Fargate task so the
  SQLite file (`jirasync.db`) persists across restarts/redeploys. **No EF provider change** — the
  current SQLite setup ships as-is. *Trade-off accepted:* this is **AWS-locked** — EFS+SQLite does
  not port to Azure, so the eventual Azure move will switch persistence then (likely to Azure
  Database for PostgreSQL via Npgsql). *Rejected for now:* RDS for PostgreSQL (portable but needs
  the Npgsql swap). **Reinforces single-instance:** concurrent Fargate tasks writing one SQLite
  file over EFS would lock/corrupt it, so exactly one task is mandatory (not just for the outbox).

- **Registry: Amazon ECR.** The CDK deploy pushes the built image here.

- **Single instance in v1.** The reconciliation sweep and write-back outbox sender are
  single-writer: two instances draining the same outbox could double-post a comment before either
  marks it Sent (idempotency dedups at submit, not at send). So pin the ECS service to
  `desiredCount=1`. Scale-out later needs a lock/leader-election or moving the outbox to a queue.

- **Secrets via AWS Secrets Manager.** Jira token, Slack bot token, and signing secret live in
  Secrets Manager and are injected as configuration/environment at runtime. Nothing secret in the
  image or repo. Local dev keeps using user-secrets.

- **Console excluded from deploy.** Only the API ships. The exclusion mechanism for the CDK
  publisher isn't documented yet — likely by not including the console resource in the deployable
  set (e.g., a deploy-time AppHost configuration or a separate AppHost entry that registers only the
  API). To be confirmed during the de-risk deploy; the executable-console resource must not become
  an ECS service.

## Risks / Trade-offs

- [`aspire deploy` AWS is experimental/preview] → Validate a minimal deploy first (the `cdk deploy` it shells to can also be run directly); if it blocks us, fall back to Azure (the eventual home).
- [No built-in RDS/EFS provisioning] → Add the datastore via a CDK construct in the AppHost, or pre-provision it and inject the connection string from Secrets Manager.
- [HTTPS needs a domain] → ALB + ACM requires a domain we control; CloudFront is the no-domain fallback. Decide before deploy.
- [Provider swap to Npgsql] → EF model is provider-agnostic, but touches connection setup, migrations, and the SQLite-only query workarounds; needs a Postgres migration set and a test pass.
- [Concurrent writers if scaled] → Pin ECS `desiredCount=1`; document the constraint.
- [Secret handling] → Secrets Manager + IAM task role; never in image; plan rotation.
- [Cost] → ECR + ECS Fargate + ALB + RDS incur ongoing cost; size minimally for v1.

## Migration Plan

1. **De-risk first:** add `Aspire.Hosting.AWS` (13.x) to the AppHost, enable the experimental
   publishers pragma, and run a **minimal `aspire deploy`** of just the API to ECS Fargate to
   confirm the CDK path, image push to ECR, and how to exclude the console. (Requires AWS creds + the AWS CDK CLI.)
2. Make the API container-publishable; ensure only the API becomes an ECS service (console excluded).
3. Add the datastore via CDK: provision **RDS for PostgreSQL**, add the Npgsql provider + Postgres migrations, and inject the connection string from Secrets Manager. (Or EFS+SQLite fallback.)
4. Add the **ALB + ACM certificate** (and domain/DNS) for public HTTPS; pin ECS `desiredCount=1`.
5. Move secrets to AWS Secrets Manager; map them to the API's configuration via the task role.
6. Run `aspire deploy`; confirm `GET /health` over the public HTTPS URL.
7. Register the Jira webhook against the public URL; verify a live event round-trips. (Slack wiring follows in its own changes.)
- *Rollback:* `cdk destroy` / tear down the stack the deploy created; local dev is unaffected.

## Open Questions

- The exact **domain/subdomain** to use (e.g., `dave.example.com`) and whether it is hosted in **Route 53** (enables automated ACM DNS validation + ALB alias) or external DNS (manual records).
- Credentials at deploy: long-lived access key/secret (you'll provide) vs. an assumed role/profile.
- Timing of the eventual Azure move — deploy AWS now and port later, or go straight to Azure if the experimental AWS path stalls?

**Resolved:** compute **ECS Fargate** via `aspire deploy`/CDK; persistence **EFS-mounted SQLite**
(no provider change, AWS-locked); public HTTPS via **ALB + ACM on a controlled domain**; region
**`us-east-1`**; **single instance**.
