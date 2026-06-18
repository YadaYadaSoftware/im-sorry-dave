## Why

Smoke testing the platform today means hand-driving Swagger or composing CLI commands and
reading raw JSON. An interactive terminal UI makes it fast to confirm a build is healthy:
pick a capability, run its happy-path flow, and see pass/fail and live state in one screen —
without external accounts (it runs against the fake backends by default). This complements
the scriptable `console-control-app`: that change is for automation; this one is for
eyes-on, interactive verification.

## What Changes

- Add a Terminal.Gui-based console application that provides an interactive smoke-test
  interface over the platform's Core services.
- Provide capability panels (starting with jira-sync-core) to view work items, simulate a
  Jira webhook, submit a write-back, and inspect the outbox and fake-Jira comments live.
- Provide a one-keystroke guided smoke-test run that executes the happy-path sequence for a
  capability and reports per-step pass/fail with details.
- Reuse the same configuration, secrets, and DI as the API/CLI, defaulting to fake backends
  so the harness is runnable on any machine; clearly flag any action that mutates a real
  external system.

## Capabilities

### New Capabilities
- `tui-smoke-test`: An interactive Terminal.Gui application for smoke testing — navigation
  shell, capability panels that drive Core services, a guided pass/fail smoke run, and live
  result/error display.

### Modified Capabilities
<!-- None. Reuses jira-sync-core (and other capabilities as they are built) via shared services. -->

## Impact

- New console project (e.g. `SorryDave.JiraSync.SmokeTui`) in the solution, referencing Core
  and the `Terminal.Gui` package; reuses `AddJiraSyncCore` for services/config.
- No new external service dependencies; uses fake backends unless real credentials are present.
- Sits alongside `console-control-app`: shared Core services, different interaction model
  (interactive TUI vs. scriptable verbs).
