## 1. ServiceDefaults

- [x] 1.1 Add the `SorryDave.JiraSync.ServiceDefaults` project (Aspire shared defaults)
- [x] 1.2 Wire the API to `AddServiceDefaults()` and map the default health/alive endpoints
- [x] 1.3 Confirm the API exposes a readiness signal usable by Aspire `WaitFor`

## 2. API admin endpoints

- [x] 2.1 Add `POST /admin/backfill` to trigger a backfill via `ReconciliationRunner`
- [x] 2.2 Add `POST /admin/reconcile` to trigger a reconciliation sweep
- [x] 2.3 Return a small JSON result (counts) for the console to display

## 3. AppHost project

- [x] 3.1 Add the `SorryDave.JiraSync.AppHost` project (Aspire AppHost SDK) to the solution
- [x] 3.2 Reference the API and the `tui-smoke-test` console projects
- [x] 3.3 Register the API resource; register the console with `WithReference(api).WaitFor(api)`
- [x] 3.4 Use `WithExplicitStart()` (or a terminal launch profile) so the interactive console is usable

## 4. Console as API client

- [x] 4.1 Add a typed `ApiClient` (HttpClient) reading the API base address from configuration
- [x] 4.2 Rework the work-items, outbox, and comments views to read from the API
- [x] 4.3 Route write-back submission and webhook simulation through the API
- [x] 4.4 Drive backfill and the guided smoke run via the API's admin endpoints
- [x] 4.5 Replace the FAKE/REAL indicator with "connected to API at <url>" and surface API-unavailable errors

## 5. Run & validation

- [x] 5.1 Run the AppHost: verify the API starts, becomes healthy, then the console starts
- [x] 5.2 Verify the console operates against the API (and real `MDP` when the API is so configured)
- [x] 5.3 Document running the AppHost and accessing the interactive console in the README
