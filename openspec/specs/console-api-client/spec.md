# console-api-client Specification

## Purpose
TBD - created by archiving change aspire-apphost. Update Purpose after archive.
## Requirements
### Requirement: Console consumes the API over HTTP

The smoke-test console SHALL perform its operations by calling the API's HTTP endpoints rather
than using Core services in-process. This includes listing work items, submitting write-backs,
viewing the outbox, and simulating a webhook.

#### Scenario: List work items via the API

- **WHEN** the console refreshes its work-items view
- **THEN** it retrieves the list from the API's work-items endpoint and displays the returned items

#### Scenario: Submit a write-back via the API

- **WHEN** the user submits a write-back from the console
- **THEN** the console posts it to the API's write-back endpoint and reflects the API's response

#### Scenario: Simulate a webhook via the API

- **WHEN** the user triggers the webhook simulation
- **THEN** the console posts a sample event to the API's webhook endpoint and refreshes from the API

### Requirement: API base address from configuration

The console SHALL read the API base address from configuration (as provided by the AppHost via
service discovery) and SHALL NOT hard-code the URL.

#### Scenario: Resolve the API from configuration

- **WHEN** the console starts
- **THEN** it determines the API base address from configuration and targets that API for all calls

### Requirement: API operations trigger backfill and reconcile

Since backfill and reconciliation run in the background in the service, the API SHALL expose
endpoints to trigger them on demand, and the console SHALL use those endpoints so its actions
and guided smoke run can drive a fresh sync.

#### Scenario: Trigger backfill from the console

- **WHEN** the user invokes backfill from the console
- **THEN** the console calls the API's backfill endpoint and the API performs a backfill, after which the work-items view reflects the result

### Requirement: Graceful handling when the API is unavailable

The console SHALL surface a clear error when the API cannot be reached and remain usable,
rather than crashing.

#### Scenario: API unreachable

- **WHEN** an API call fails because the API is not reachable
- **THEN** the console shows an error message indicating the API is unavailable and stays responsive

