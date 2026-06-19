# aspire-orchestration Specification

## Purpose
TBD - created by archiving change aspire-apphost. Update Purpose after archive.
## Requirements
### Requirement: Aspire AppHost orchestrates the solution

The solution SHALL include a .NET Aspire AppHost that registers the API and the smoke-test
console as managed resources and serves as the single entry point for running them together.

#### Scenario: Running the AppHost starts the resources and dashboard

- **WHEN** a developer runs the Aspire AppHost
- **THEN** the API and the console are started as managed resources and the Aspire dashboard is available with their logs and status

### Requirement: Console starts only after the API is healthy

The AppHost SHALL order startup so the console begins only after the API resource has started
and reported healthy, based on the API health endpoint rather than a fixed delay.

#### Scenario: Console waits for the API

- **WHEN** the AppHost starts
- **THEN** the API resource starts first and the console resource does not start until the API reports healthy

#### Scenario: Ordering is health-gated, not timed

- **WHEN** the AppHost decides whether to start the console
- **THEN** it waits on the API's health check, not an arbitrary timer

#### Scenario: API never becomes healthy

- **WHEN** the API resource fails to reach a healthy state
- **THEN** the console resource is not started

### Requirement: API endpoint injected into the console

The AppHost SHALL pass the API's base address to the console via service discovery, so the
console resolves the API without a hard-coded URL.

#### Scenario: Console resolves the API by reference

- **WHEN** the console resource starts under the AppHost
- **THEN** it receives the API's base address through configuration/service discovery provided by the AppHost

### Requirement: Well-defined readiness via ServiceDefaults

The API SHALL expose Aspire-style health endpoints (via a shared ServiceDefaults configuration)
so the AppHost's health-gated ordering has a definite readiness signal.

#### Scenario: API reports health for orchestration

- **WHEN** the AppHost queries the API's readiness
- **THEN** the API answers via its health endpoint and the AppHost uses that to gate the console

### Requirement: Interactive console launch is documented

The AppHost SHALL launch the interactive console so its Terminal.Gui UI remains usable in a
terminal, since it does not render inside the Aspire dashboard, and this launch behavior MUST be
documented in the run instructions.

#### Scenario: Console is usable, not trapped in the dashboard

- **WHEN** the console resource is started by the AppHost
- **THEN** it is launched so the interactive UI is usable in a terminal, and the run instructions document how to access it

