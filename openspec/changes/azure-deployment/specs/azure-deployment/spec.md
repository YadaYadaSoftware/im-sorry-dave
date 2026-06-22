## ADDED Requirements

### Requirement: Deploy to Azure from the AppHost, selectable alongside AWS

The AppHost SHALL support deploying to Azure as well as AWS, with the target cloud chosen at deploy
time. Selecting AWS SHALL behave exactly as before; selecting Azure SHALL publish the API to Azure.

#### Scenario: Azure selected

- **WHEN** an operator runs the deploy with the cloud selector set to Azure
- **THEN** the AppHost provisions and updates the Azure resources for the API and does not touch the
  AWS stack

#### Scenario: AWS remains the default and unchanged

- **WHEN** an operator deploys without selecting a cloud (or selects AWS)
- **THEN** the AWS ECS/EFS/SSM deployment runs exactly as it did before this change

### Requirement: Azure Container Apps single instance

The Azure deployment SHALL run the API as a single Azure Container Apps replica, because the
SQLite-backed background workers are single-writer.

#### Scenario: Single replica enforced

- **WHEN** the API is deployed to Azure
- **THEN** the container app runs with a single replica (min = max = 1), so no two instances write
  the database concurrently

### Requirement: Durable persistence on Azure Files

On Azure the API SHALL store its SQLite database on a mounted Azure Files share so data survives
revision rollovers and restarts, using the same connection-string path as on AWS.

#### Scenario: Database persists across restarts

- **WHEN** the Azure deployment restarts or rolls to a new revision
- **THEN** previously mirrored work items and outbox records are still present (the SQLite file lives
  on the Azure Files mount at `/data`)

### Requirement: Secrets from Azure Key Vault

On Azure the API SHALL resolve its secrets from **Azure Key Vault** via an app-side configuration
provider, authorized by the container's managed identity, with secret names mapping to the same
configuration keys. Secrets SHALL NOT be baked into the image or source, and adding a secret SHALL
NOT require redeploying the application.

#### Scenario: Secrets resolved from Key Vault at startup

- **WHEN** the service starts on Azure
- **THEN** it loads its secrets (Jira token, webhook secret, Slack bot token + signing secret) from
  Key Vault, mapped to the same config keys it uses on AWS, with none present in the image or repo

#### Scenario: New secret needs no redeploy

- **WHEN** a new secret is added to the Key Vault
- **THEN** the service resolves it on its next start using the existing managed-identity access, with
  no application redeploy

### Requirement: Public HTTPS endpoint on Azure

The Azure deployment SHALL expose the API over public HTTPS so Jira webhooks and the Slack app can
reach it.

#### Scenario: HTTPS endpoint available

- **WHEN** the API is deployed to Azure
- **THEN** it is reachable over HTTPS at a managed Azure Container Apps endpoint with a valid
  certificate

### Requirement: The TUI can drive the Azure deployment

The smoke-test TUI SHALL be able to target the Azure deployment as one of its selectable API targets,
including sending the webhook secret to the secured webhook endpoint.

#### Scenario: Azure target selectable

- **WHEN** the operator selects the `azure` target in the TUI
- **THEN** the console drives the Azure-deployed API (listing work items, and simulating a webhook
  with the target's webhook secret accepted by the secured endpoint)
