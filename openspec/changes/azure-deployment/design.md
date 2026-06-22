## Context

The AWS deployment runs the API on ECS Fargate behind an ALB, with SQLite on an EFS mount and
secrets in SSM Parameter Store, all driven from the AppHost's publish branch. The secrets layer was
deliberately built provider-swappable (`secrets-configuration-convention` calls out Azure Key Vault
as the drop-in), and persistence is just SQLite on a mounted file share. This change adds an Azure
path that reuses those seams: Azure Container Apps for compute, Azure Files for the SQLite mount,
Azure Key Vault for secrets — selected at deploy time, with AWS untouched.

## Goals / Non-Goals

**Goals:**
- One codebase deploys to **either** AWS or Azure, chosen at deploy.
- Mirror the AWS shape to minimize divergence (single container, SQLite on a file mount, managed
  secret store, public HTTPS).
- Reuse EF/SQLite/migrations and the config-key contract **unchanged**.

**Non-Goals:**
- PostgreSQL / a database rearchitecture (explicitly chosen against for this change).
- A custom domain on Azure (default ACA FQDN HTTPS now; Azure DNS later).
- Removing or changing the AWS deployment.
- Multi-replica / horizontal scale (SQLite stays single-writer).

## Decisions

### A cloud selector in the AppHost; AWS branch unchanged
**Decision:** Add a deploy-time selector — a `--cloud` argument / config value, default `aws`. The
publish branch dispatches: `aws` → today's `AddAWSCDKEnvironment` + `PublishAsECSFargateServiceWithALB`
+ EFS + SSM transport; `azure` → the new Azure branch. *Why:* keeps the working AWS path intact and
makes "deploy to Azure" a flag. *Alternative:* a second AppHost project — rejected (duplicates the
resource graph).

### Azure Container Apps, single replica
**Decision:** Publish the API to **Azure Container Apps** with `minReplicas = maxReplicas = 1`. *Why:*
ACA is the standard Aspire Azure target and the closest analog to Fargate (serverless containers,
managed ingress + TLS); the single replica preserves the SQLite single-writer invariant (the ACA
analog of the AWS single-instance + non-overlapping deploy). *Trade-off:* brief downtime on revision
rollover — acceptable, same as AWS.

### SQLite on an Azure Files mount at `/data`
**Decision:** Provision a storage account + file share, mount it into the container at `/data`, and
keep `ConnectionStrings:JiraSync = Data Source=/data/jirasync.db`. *Why:* the EFS analog — same
SQLite, same migrations, durable across revisions. *Risk:* SQLite locking over Azure Files (SMB) has
the same caveats as over EFS (NFS); the single replica mitigates it. *Note:* mounting an Azure Files
volume into an ACA container via Aspire may require customizing the generated Bicep / container-app
definition (the Azure analog of the CDK construct callbacks used on AWS).

### Secrets via an Azure Key Vault config provider (the convention's swap)
**Decision:** Add `builder.Configuration.AddAzureKeyVault(...)` to `Program.cs`, gated on
`Azure:KeyVaultUri` (set by the AppHost for Azure), as a sibling to the SSM provider. Key Vault
secret names map `--` → `:` (`Jira--ApiToken` → `Jira:ApiToken`), so the app reads the same keys.
The container authenticates with its **managed identity** (granted `get`/`list` on the vault). The
AppHost **transports** the secrets into Key Vault on deploy (the SSM-transport analog), and the
fail-fast startup check is unchanged. *Why:* exactly the "swap the provider per cloud" the secrets
spec anticipated — config keys, app code, and the fail-fast all stay the same.

### HTTPS via ACA managed ingress; custom domain deferred
**Decision:** Use ACA external ingress, which yields an HTTPS `*.azurecontainerapps.io` FQDN with a
managed certificate — no ACM/Route 53 analog needed to get TLS. A custom domain
(`jsg.appcloud.systems` via Azure DNS or a CNAME) is a follow-up. *Why:* gets a working public HTTPS
endpoint (for webhooks) with the least Azure DNS plumbing.

### TUI gains an `azure` target (config only)
**Decision:** Add `azure` to `ApiTargets` (the ACA URL) and its webhook secret to user-secrets/SSM/
Key Vault. The targeting feature already resolves N targets and applies a per-target webhook secret —
no TUI code change, only configuration.

## Risks / Trade-offs

- [Aspire Azure publishing maturity] → the Azure APIs are evolving (as the AWS publishers were);
  budget iteration, validate with a synth/preview before a billable deploy, especially the Azure
  Files mount.
- [SQLite over Azure Files SMB locking] → single replica + non-overlapping revision rollover; same
  posture as EFS. If it proves fragile, PostgreSQL is the escape hatch (a separate change).
- [Two clouds to keep working] → the selector keeps them isolated; CI/manual-verify both paths.
- [Cost] → ACA + storage + Key Vault; comparable order to the AWS stack. Tear down when idle.

## Migration Plan

1. Add the Azure packages (`Aspire.Hosting.Azure.AppContainers`, `Aspire.Hosting.Azure.KeyVault`,
   `Azure.Extensions.AspNetCore.Configuration.Secrets`).
2. AppHost: add the `--cloud` selector and the Azure branch (ACA + Azure Files mount + Key Vault +
   secret transport + `Azure:KeyVaultUri` env + single replica).
3. API: add the Key Vault config provider gated on `Azure:KeyVaultUri`.
4. Provision secrets into Key Vault (via the transport); deploy to Azure; verify `/health`,
   `/workitems`, webhook 401/200, and SQLite persistence on Azure Files.
5. TUI: add the `azure` target + its webhook secret.
- *Rollback:* the default `--cloud aws` path is unchanged; Azure resources are a separate stack to
  delete.

## Open Questions

- Custom domain on Azure (Azure DNS vs CNAME from the existing zone) — deferred to a follow-up.
- Whether to later unify secret transport so all secrets (not just Slack) flow through the AppHost on
  both clouds — tracked in `deployment-playbook` as a possible follow-up.
