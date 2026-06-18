## Why

Running the platform locally means launching projects in the right order by hand, and today
the console doesn't actually exercise the running API — it duplicates behavior in-process. We
want one command that brings up the API and then a console that is a real client of that API
(and, through it, of real Jira), with a dashboard for logs and health. .NET Aspire provides an
AppHost that orchestrates the projects, injects the API endpoint into the console, and enforces
"start the API, then the console" via health-gated ordering.

## What Changes

- Add a .NET Aspire AppHost project that orchestrates the solution and is the single entry point.
- **Rewire the `tui-smoke-test` console to call the API over HTTP** instead of using Core in-process: it lists work items, submits write-backs, views the outbox, and simulates webhooks through the API's endpoints. Through the API it now operates on whatever Jira the API is configured for (real `MDP` when credentials are present).
- Register the API as a managed resource and start the console **only after the API is running and healthy** (Aspire `WaitFor` gated on the API health endpoint).
- Have Aspire inject the API base address into the console via service discovery, so the console needs no hard-coded URL.
- Add a ServiceDefaults project and wire the API to it (health checks, OpenTelemetry, resilience) so readiness is well-defined.
- Add the few API endpoints the console needs but the API doesn't yet expose (trigger backfill / reconcile); webhook simulation reuses the existing `POST /webhooks/jira`.
- Document how the interactive console is launched/accessed, since a Terminal.Gui UI needs a real terminal and does not render inside the Aspire dashboard.

## Capabilities

### New Capabilities
- `aspire-orchestration`: The Aspire AppHost that registers the solution's projects, enforces API-before-console start ordering via health-gated `WaitFor`, injects the API endpoint into the console, and serves as the single run entry point.
- `console-api-client`: The smoke-test console consumes the API over HTTP (work items, write-back, outbox, webhook simulation), reading the API base address from configuration rather than calling Core in-process.

### Modified Capabilities
<!-- tui-smoke-test is an active (not yet archived) change with no main spec to delta against;
     its API-client rework is captured here as the new `console-api-client` capability. -->

## Impact

- New `SorryDave.JiraSync.AppHost` and `SorryDave.JiraSync.ServiceDefaults` projects (Aspire 9.x, SDK-based — no workload install). The AppHost references the API and the console.
- The console (`tui-smoke-test`) is reworked from in-process Core calls to a typed `HttpClient` against the API; it no longer needs its own database or Jira configuration.
- The API gains small admin endpoints to trigger backfill/reconcile on demand (used by the console's actions and guided smoke run).
- Behavior shift: the console now reflects the **API's** backend. With the API's current `MDP` credentials, the console operates on real Jira rather than the in-memory fake. The AppHost run is only as "fake" as the API is configured to be.
- New dependency on .NET Aspire hosting packages and `Microsoft.Extensions.ServiceDiscovery`.
