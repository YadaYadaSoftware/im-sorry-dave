## Context

The platform is event-driven and currently smoke-tested by hand via Swagger or raw CLI
output. An interactive Terminal.Gui app gives eyes-on verification: drive a capability,
watch live state, and run a guided pass/fail sequence — against fake backends by default so
it runs anywhere. It reuses the same Core services as the API and the `console-control-app`
CLI to avoid behavioral drift.

## Goals / Non-Goals

**Goals:**
- An interactive TUI to smoke-test capabilities, starting with jira-sync-core.
- Live views of work items, the write-back outbox, and fake-Jira comments.
- A guided, ordered smoke run with per-step pass/fail and overall result.
- Identical behavior to the API/CLI by reusing shared services; safe by default.

**Non-Goals:**
- Replacing automated tests (`dotnet test`) or the scriptable CLI.
- A production operations console or a remote/multi-user UI.
- Implementing capability logic — the TUI only drives existing services.

## Decisions

- **Terminal.Gui v2 over the .NET Generic Host.** Build the host (shared `AddJiraSyncCore`),
  resolve services, then run the Terminal.Gui application loop. Background hosted services are
  not started; the TUI invokes runners (e.g. `ReconciliationRunner`, `WriteBackSender`) on
  demand so each action does discrete, observable work. *Alternative:* a bespoke console
  menu — rejected (Terminal.Gui gives panels, forms, tables, and input handling for free, as
  the user requested).
- **Async work off the UI thread, marshalled back.** Service calls run on a background task;
  results are applied to views via `Application.Invoke` so the UI stays responsive and never
  blocks the render loop.
- **Panel-per-capability, driven by shared services.** Each panel calls the same services the
  API uses (e.g. `IWriteBackService`, `WebhookProcessor`, `JiraSyncDbContext`). New panels are
  added as capabilities land. Keeps one source of truth.
- **Guided smoke run = an ordered list of steps with assertions.** Each step performs an action
  and verifies an observable outcome (e.g. "write-back reaches Sent"), capturing pass/fail and
  detail. Mirrors the manual happy path proven during jira-sync-core review.
- **Fake-by-default, explicit confirmation for real mutations.** The status bar shows the mode;
  in real mode, mutating actions require confirmation, consistent with the CLI's `--dry-run`
  philosophy.

## Risks / Trade-offs

- [UI thread blocked by a slow service call] → Run work async; marshal updates with `Application.Invoke`.
- [Terminal.Gui rendering issues across terminals/CI] → Target a real interactive terminal;
  the harness is for local/manual use, not headless CI (use `dotnet test` there).
- [Accidental writes to real Jira] → Fake-by-default + mode indicator + confirmation on real mutations.
- [Drift from API behavior] → Reuse the exact Core service registrations.
- [Outbox is timer-driven] → The smoke run invokes `WriteBackSender.ProcessDueAsync` directly so
  delivery is observed immediately rather than waiting for the poll interval.

## Migration Plan

1. Add the `SorryDave.JiraSync.SmokeTui` project referencing Core and `Terminal.Gui`.
2. Build the host + Terminal.Gui shell (menu, status bar, panel navigation).
3. Implement the jira-sync-core panel (work items, webhook sim, write-back, outbox, comments).
4. Implement the guided smoke run for jira-sync-core; add panels/runs for other capabilities as they ship.
- *Rollback:* the project is additive; removing it affects nothing else.

## Open Questions

- Terminal.Gui v1 vs v2 — default to v2 unless the team standardizes otherwise.
- Should the guided run be exportable (e.g. write a pass/fail summary to a file) for sharing?
- Should panels talk to in-process services directly (chosen) or to a running API over HTTP?
