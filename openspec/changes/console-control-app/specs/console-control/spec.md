## ADDED Requirements

### Requirement: Capability-grouped command structure

The console application SHALL expose its functions as verb-based commands grouped by
capability (e.g. `workitem`, `sync`, `writeback`, `slack`, `summarize`, `github`, `openspec`),
and SHALL provide discoverable help listing the available groups and commands.

#### Scenario: Top-level help lists capability groups

- **WHEN** the console is run with `--help` or no arguments
- **THEN** it prints the available command groups and a one-line description of each, and exits successfully

#### Scenario: Group help lists its commands

- **WHEN** the console is run with a group name and `--help` (e.g. `sync --help`)
- **THEN** it prints the commands in that group with their options

#### Scenario: Unknown command is reported

- **WHEN** an unrecognized command or group is supplied
- **THEN** the console prints an error naming the unknown command and exits with a non-zero code

### Requirement: Shared host, configuration, and services

The console SHALL load the same configuration and secrets as the web host and SHALL invoke
the same Core services, so a command and its API equivalent produce identical behavior.

#### Scenario: Commands use shared configuration

- **WHEN** a command runs
- **THEN** it reads configuration/secrets from the same providers as the API (settings + user-secrets/secret store) and uses the registered capability services

#### Scenario: Real vs fake backends honored

- **WHEN** no external credentials are configured for a capability
- **THEN** the console uses that capability's fake/in-memory backend (matching the API), so commands are runnable for local review

### Requirement: Consistent output and exit codes

The console SHALL return exit code 0 on success and a non-zero code on failure, and SHALL
support a `--json` option that emits machine-readable output for scripting.

#### Scenario: Success exit code

- **WHEN** a command completes successfully
- **THEN** the process exits with code 0

#### Scenario: Failure exit code with message

- **WHEN** a command fails (invalid input, not-found, or a downstream error)
- **THEN** the console prints a human-readable error to stderr and exits with a non-zero code

#### Scenario: JSON output for scripting

- **WHEN** a command is run with `--json`
- **THEN** its primary result is emitted as JSON on stdout with no decorative text

### Requirement: Safe-by-default for mutating commands

Commands that mutate an external system (Jira, Slack, GitHub) SHALL support a global
`--dry-run` that reports the intended action without performing it, and SHALL clearly
indicate when an action was actually performed.

#### Scenario: Dry-run performs no mutation

- **WHEN** a mutating command is run with `--dry-run`
- **THEN** the console reports what it would do and makes no change to any external system

#### Scenario: Performed action is confirmed

- **WHEN** a mutating command runs without `--dry-run`
- **THEN** the console reports the action it performed and its result
