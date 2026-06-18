## Context

Developers want a single command to run the platform with correct ordering, and the console
should genuinely exercise the running API (and through it, real Jira) rather than duplicating
behavior in-process. .NET Aspire's AppHost orchestrates the projects, injects endpoints via
service discovery, and gates startup on health. This change adds the AppHost + ServiceDefaults
and reworks the `tui-smoke-test` console into an API client.

## Goals / Non-Goals

**Goals:**
- One entry point (the AppHost) that starts the API, then the console, ordered by API health.
- Console talks to the API over HTTP, resolving the endpoint via Aspire service discovery.
- Well-defined API readiness via ServiceDefaults health endpoints.
- Runs locally; the console reflects whatever backend the API is configured for.

**Non-Goals:**
- Production deployment topology (this targets local/dev orchestration).
- Changing how the API itself talks to Jira (unchanged; still owns credentials/config).
- Containerizing the apps (Aspire can, but it's out of scope here).

## Decisions

- **Aspire 9.x, SDK-based AppHost.** Add `SorryDave.JiraSync.AppHost` (Aspire AppHost SDK) and
  `SorryDave.JiraSync.ServiceDefaults`. No workload install — Aspire 9 ships as NuGet packages.
- **Health-gated ordering with `WaitFor`.** `var api = builder.AddProject<…Api>("api");` then
  `builder.AddProject<…SmokeTui>("console").WithReference(api).WaitFor(api);` so the console
  starts only after the API is healthy and receives the API endpoint via service discovery.
  *Alternative:* a startup delay — rejected (races, flaky).
- **ServiceDefaults on the API.** Adds OpenTelemetry, default health/alive endpoints, and
  HttpClient resilience + service discovery. `WaitFor` uses the health endpoint as the readiness
  signal. The API keeps its existing `/health`; ServiceDefaults supplies the Aspire-standard ones.
- **Console becomes a thin API client.** Replace the in-process Core calls in the TUI with a
  typed `HttpClient` (`ApiClient`) whose base address comes from configuration
  (`services__api__http__0` / `ApiBaseUrl`) injected by Aspire. The console no longer needs its
  own SQLite DB or Jira config. Panels and the guided smoke run call API endpoints.
- **New API admin endpoints.** Backfill/reconcile are background-only today; add
  `POST /admin/backfill` and `POST /admin/reconcile` so the console (and guided run) can drive a
  sync on demand. Webhook simulation reuses `POST /webhooks/jira`.
- **Interactive TUI under Aspire.** A Terminal.Gui app cannot render in the dashboard. Use
  `WithExplicitStart()` on the console so it does not auto-launch into redirected output; the
  developer starts it from the dashboard/terminal, or runs the TUI in a terminal while the
  AppHost provides the API. Documented either way.
- **Backend honesty.** The console now reflects the API's backend: with the API's current `MDP`
  credentials it shows real Jira data. The status indicator becomes "talking to API at <url>"
  rather than FAKE/REAL (which is now the API's concern).

## Risks / Trade-offs

- [Console depends on API availability] → `WaitFor` gates startup; the client surfaces clear errors if the API drops.
- [New admin endpoints mutate state] → Keep them minimal and intended for local/dev; guard/secure before any non-dev exposure.
- [Interactive TUI doesn't fit dashboard streaming] → explicit-start / terminal launch + docs.
- [Console now hits real MDP via the API] → made explicit in UI and docs; the API's config decides fake vs real.
- [Aspire prerequisites] → document required .NET/Aspire SDK + packages; AppHost is dev-only.

## Migration Plan

1. Add `ServiceDefaults`; wire the API to `AddServiceDefaults()` / map default health endpoints.
2. Add the `AppHost` project; reference the API and console; express `WithReference(api).WaitFor(api)`.
3. Add `POST /admin/backfill` and `POST /admin/reconcile` to the API.
4. Rework the console: add `ApiClient`, point panels/guided run at the API, read the base address from config, remove in-process Core data calls.
5. Add `WithExplicitStart()` (or terminal launch) for the console; document running the AppHost.
- *Rollback:* the console can revert to in-process Core; the AppHost/ServiceDefaults are additive and removable.

## Open Questions

- Should the AppHost also orchestrate the planned `console-control-app` CLI once it exists?
- Should the API admin endpoints be gated (auth/flag) even in dev?
- Keep a fallback "in-process" mode in the console for offline use, or API-only?
