## Why

The platform was built "eventually Azure": the secrets layer is provider-swappable and the container
is portable. This change cashes that in — a working **Azure** deployment path **alongside** the
existing AWS one, selected at deploy time, so the same codebase runs on either cloud. It mirrors the
AWS shape (single container, SQLite on a mounted file share, a managed secret store, public HTTPS)
to keep divergence minimal, per the chosen **SQLite-on-Azure-Files + Azure Container Apps** approach.

## What Changes

- **AppHost — a cloud selector.** Add a deploy-time selector (e.g. `--cloud azure`, default `aws`).
  The current AWS branch is unchanged; a new **Azure branch** publishes the API to **Azure Container
  Apps** (single replica — SQLite is single-writer), mounts **Azure Files** at `/data` for the
  SQLite database (the EFS analog), provisions an **Azure Key Vault**, and **transports the secrets**
  into Key Vault on deploy (the SSM-transport analog).
- **API — an Azure Key Vault configuration provider.** A sibling to the existing SSM provider, gated
  on an Azure flag (e.g. `Azure:KeyVaultUri`), realizing the secrets convention's "swap the provider
  per cloud." Secret names map `--` → `:` (`Jira--ApiToken` → `Jira:ApiToken`). The app reads the
  same config keys; nothing else changes. **EF/SQLite/migrations are reused unchanged** — the
  connection string still points at `/data/jirasync.db` (now an Azure Files mount).
- **TUI — an `azure` target.** Add `azure` to `ApiTargets` (the ACA URL) plus its webhook secret, so
  the console drives the Azure instance exactly like `local`/`aws` (the targeting feature already
  supports N targets — this is config only).
- **HTTPS** via ACA's **managed ingress + TLS** on the default `*.azurecontainerapps.io` FQDN; a
  custom domain on Azure DNS is a follow-up.
- **AWS stays fully working** — Azure is additive; the cloud is chosen at deploy.

## Capabilities

### New Capabilities
- `azure-deployment`: deploying the platform to **Azure** (Azure Container Apps + Azure Files +
  Azure Key Vault + managed HTTPS) via the AppHost, selectable alongside the AWS path, with the
  TUI able to drive the Azure instance.

## Impact

- `src/SorryDave.JiraSync.AppHost` — `AppHost.cs` (cloud selector + Azure branch) and new
  `Aspire.Hosting.Azure.*` packages (Container Apps, Key Vault).
- `src/SorryDave.JiraSync.Api` — `Program.cs` (Azure Key Vault provider) and the
  `Azure.Extensions.AspNetCore.Configuration.Secrets` package.
- `src/SorryDave.JiraSync.SmokeTui` — `appsettings.json` (`azure` target) + user-secret for its
  webhook secret.
- Realizes the drop-in anticipated by `secrets-configuration-convention`; sits beside
  `aws-aspire-deployment`. No change to `jira-sync-core`, `slack-channel-provisioning`, or the EF
  migrations.
- **Note:** Aspire's Azure publishing APIs are evolving (as the AWS ones were) and may require
  iteration during implementation, especially the Azure Files volume mount on ACA.
