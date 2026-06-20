## ADDED Requirements

### Requirement: Configured API targets

The TUI SHALL read a named set of API targets from configuration, where each target provides the
base URL and any authentication detail (the webhook shared secret) needed to drive that backend.
Sensitive per-target values SHALL be sourced from user-secrets or environment rather than the
repository.

#### Scenario: Multiple targets configured

- **WHEN** the application starts with `ApiTargets` defining both `local` and `aws`
- **THEN** both targets are available for selection, each with its configured base URL

#### Scenario: Per-target secret from user-secrets

- **WHEN** a target's webhook secret is set via user-secrets or environment
- **THEN** the TUI uses it for that target, and the value does not appear in the repository or the UI

#### Scenario: No targets configured falls back

- **WHEN** no `ApiTargets` are configured
- **THEN** the TUI falls back to a localhost default target and still launches

### Requirement: Active target selection

The TUI SHALL operate against a single active target chosen at startup from configuration or the
command line, and SHALL allow the operator to switch the active target at runtime without
restarting. Switching the target SHALL reconnect subsequent actions to the newly selected backend.

#### Scenario: Default target at startup

- **WHEN** `ActiveApiTarget` names a configured target
- **THEN** the TUI connects to that target on launch

#### Scenario: Override target on the command line

- **WHEN** the operator passes a target selection on the command line (e.g. `--target aws`)
- **THEN** the TUI connects to that target instead of the configured default

#### Scenario: Switch target at runtime

- **WHEN** the operator selects a different target from the in-app target menu
- **THEN** the TUI reconnects to that target and subsequent actions reach the newly selected backend

### Requirement: Per-target webhook authentication

The TUI SHALL apply the active target's webhook shared secret when calling the secured inbound
webhook endpoint, so simulating a webhook succeeds against a backend that rejects unsigned requests,
and SHALL omit the secret when the active target defines none.

#### Scenario: Secured target receives the secret

- **WHEN** the active target defines a webhook secret and the operator simulates a webhook
- **THEN** the request carries the secret and the secured API accepts it

#### Scenario: Unsecured target omits the secret

- **WHEN** the active target defines no webhook secret and the operator simulates a webhook
- **THEN** the request is sent without a secret and the unsecured API accepts it

### Requirement: Aspire-injected endpoint as an implicit target

When launched by the Aspire AppHost, the TUI SHALL treat the injected API endpoint as an implicit
target and use it by default, unless an explicit active target is configured or selected.

#### Scenario: Launched by the AppHost

- **WHEN** the AppHost injects the API endpoint via service discovery and no explicit target is set
- **THEN** the TUI connects to the injected endpoint

#### Scenario: Explicit selection wins over injection

- **WHEN** an explicit active target is configured or chosen on the command line
- **THEN** the TUI uses that target even if an injected endpoint is present
