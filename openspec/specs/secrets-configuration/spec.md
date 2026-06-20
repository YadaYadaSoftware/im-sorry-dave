# secrets-configuration Specification

## Purpose
TBD - created by archiving change secrets-configuration-convention. Update Purpose after archive.
## Requirements
### Requirement: Secrets resolved from layered configuration

The application SHALL obtain every secret as a configuration key through the standard configuration
system, without the application code knowing or depending on the source. Sources SHALL be layered
with a defined precedence — committed defaults lowest, then the production managed store, then
environment variables, then local developer secrets — so any secret can be supplied or overridden
per environment.

#### Scenario: Source-agnostic read

- **WHEN** the application needs a secret (e.g. the Jira API token)
- **THEN** it reads it as a configuration key regardless of whether the value came from the managed store, an environment variable, or local developer secrets

#### Scenario: Environment variable overrides the store

- **WHEN** a secret is present both in the managed store and as an environment variable
- **THEN** the environment variable value is used

### Requirement: No secrets in source control

Committed configuration files SHALL contain only non-secret structure and defaults. Real secret
values SHALL come only from the production managed store, environment variables, or a local
developer secret store, and SHALL NOT be committed.

#### Scenario: Committed config has no secret values

- **WHEN** the repository is inspected
- **THEN** `appsettings.json` (and other committed config) contains no real secret values, only non-secret keys and defaults

### Requirement: Production secrets from a portable managed-store provider

In a deployed environment the application SHALL load its secrets from a managed secret store through
a configuration provider that maps entries under a single namespace/path to configuration keys,
authorized by a single grant on that namespace. Adding a new secret under the namespace SHALL NOT
require a new per-secret authorization, a deployment-artifact change, or a redeploy. The provider
SHALL be replaceable per cloud (e.g. AWS SSM Parameter Store, Azure Key Vault) without changing how
the application consumes secrets.

#### Scenario: New secret needs no new grant or redeploy

- **WHEN** a new secret is added under the managed-store namespace
- **THEN** the application resolves it on its next start with no new authorization grant, no deployment-artifact change, and no redeploy

#### Scenario: Secrets absent from the deployment artifact

- **WHEN** the deployed service is running
- **THEN** its secret values are not present in the container image, the deployment/task definition, or the repository

#### Scenario: Provider swapped per cloud

- **WHEN** the platform moves to a different cloud and the managed-store provider is replaced
- **THEN** the configuration keys and the application's secret-consuming code are unchanged

### Requirement: Fail fast on missing required secrets

If a required secret cannot be resolved at startup, the application SHALL fail to start rather than
run misconfigured, so the platform never silently operates without its credentials.

#### Scenario: Required secret unresolved

- **WHEN** the production managed store is unreachable or a required secret is absent at startup
- **THEN** the service fails to start (and is restarted by the platform) rather than starting without the secret

### Requirement: Local development secrets stay local

In local development the application SHALL resolve real secrets from a developer secret store
(user-secrets) layered above committed defaults, so contributors run against real backends without
the production managed store and without committing values.

#### Scenario: Developer secret store supplies values

- **WHEN** a developer sets secrets in user-secrets and runs the application locally
- **THEN** those values are used and no managed-store provider is required

