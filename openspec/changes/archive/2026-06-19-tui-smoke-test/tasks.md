## 1. Project & host

- [x] 1.1 Add the `SorryDave.JiraSync.SmokeTui` console project to the solution, referencing Core
- [x] 1.2 Add the `Terminal.Gui` package
- [x] 1.3 Build a Generic Host that calls the shared `AddJiraSyncCore` (no hosted background services)

## 2. TUI shell

- [x] 2.1 Initialize the Terminal.Gui application loop with a top menu and status bar
- [x] 2.2 Show the active backend mode (FAKE/REAL) in the status bar
- [x] 2.3 Implement panel navigation and a clean quit (exit code 0)
- [x] 2.4 Run service calls async and marshal results back with `Application.Invoke`

## 3. jira-sync-core panel

- [x] 3.1 Work-items view (list with key/status/assignee; backfill action)
- [x] 3.2 Webhook simulator (submit a sample issue-updated event through the processor)
- [x] 3.3 Write-back form (submit) + outbox view + fake-Jira comments view
- [x] 3.4 Require confirmation for mutating actions when in REAL mode

## 4. Guided smoke run

- [x] 4.1 Define an ordered step model with per-step assertions and pass/fail capture
- [x] 4.2 Implement the jira-sync-core sequence (backfill → submit write-back → drain outbox → verify Sent)
- [x] 4.3 Render per-step status and an overall pass/fail result

## 5. Robustness & docs

- [x] 5.1 Surface errors in the UI without crashing the loop
- [x] 5.2 Document how to launch and use the smoke-test TUI in the README
