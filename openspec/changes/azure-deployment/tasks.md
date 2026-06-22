# Tasks

## 1. Packages

- [ ] 1.1 Add Azure Aspire packages to the AppHost (`Aspire.Hosting.Azure.AppContainers`,
      `Aspire.Hosting.Azure.KeyVault`, and storage as needed for the Azure Files share)
- [ ] 1.2 Add `Azure.Extensions.AspNetCore.Configuration.Secrets` (+ `Azure.Identity`) to the API

## 2. AppHost — cloud selector & Azure branch

- [ ] 2.1 Add a deploy-time cloud selector (`--cloud azure|aws`, default `aws`); dispatch the publish
      branch on it, leaving the AWS branch unchanged
- [ ] 2.2 Azure branch: publish the API to **Azure Container Apps** with a single replica (min = max = 1)
- [ ] 2.3 Provision a storage account + file share and **mount Azure Files at `/data`**; keep
      `ConnectionStrings__JiraSync = Data Source=/data/jirasync.db` (customize the ACA/Bicep as needed)
- [ ] 2.4 Provision an **Azure Key Vault**; grant the container app's managed identity get/list; set
      `Azure__KeyVaultUri` on the app
- [ ] 2.5 **Transport secrets** into Key Vault on deploy (the SSM-transport analog), reading from the
      AppHost's config/user-secrets, with secret names mapped `:` → `--`

## 3. API — Azure Key Vault provider

- [ ] 3.1 In `Program.cs`, when `Azure:KeyVaultUri` is set, add the Azure Key Vault configuration
      provider (managed-identity auth) as a sibling to the SSM provider, below env vars
- [ ] 3.2 Confirm the `--` → `:` secret-name mapping resolves the existing config keys; the fail-fast
      startup check is unchanged

## 4. Provision, deploy & verify

- [ ] 4.1 Validate via the Azure publish/preview (no billable resources) before deploying
- [ ] 4.2 Deploy to Azure; populate Key Vault via the transport
- [ ] 4.3 Verify `/health`, `/workitems` (real Jira), webhook 401 unsigned / 200 signed, and SQLite
      persistence across a revision restart

## 5. TUI — Azure target

- [ ] 5.1 Add the `azure` target to the TUI `appsettings.json` (the ACA URL) and its
      `ApiTargets:azure:WebhookSecret` to user-secrets
- [ ] 5.2 Verify the TUI can switch to `azure`, list MDP work items, and simulate a webhook (accepted)

## 6. Docs

- [ ] 6.1 Document the Azure deploy (cloud selector, Key Vault, Azure Files, ACA endpoint) and the
      AWS↔Azure parity in the README / `INSTRUCTIONS.md`; note the Azure-Key-Vault realization of the
      secrets convention
