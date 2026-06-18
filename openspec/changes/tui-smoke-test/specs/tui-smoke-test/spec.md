## ADDED Requirements

### Requirement: Interactive Terminal.Gui shell

The application SHALL present a keyboard-navigable Terminal.Gui interface with a top-level
menu of capability panels and a status bar showing the active backend mode (fake or real) and
key actions.

#### Scenario: Launch shows the capability menu

- **WHEN** the application starts
- **THEN** it renders a Terminal.Gui window with a menu listing the available capability panels and a status bar

#### Scenario: Navigate between panels

- **WHEN** the user selects a capability from the menu
- **THEN** the application shows that capability's panel and keeps the menu/status bar available

#### Scenario: Quit cleanly

- **WHEN** the user invokes the quit action
- **THEN** the application shuts down the Terminal.Gui loop and exits with code 0

### Requirement: Backend mode is visible and safe by default

The application SHALL use the same configuration/services as the API and SHALL default to the
fake backends when no external credentials are configured, displaying the active mode so the
user knows whether actions hit real systems.

#### Scenario: Fake mode indicated

- **WHEN** no external credentials are configured
- **THEN** the status bar indicates "FAKE" backend mode and capability actions operate against the in-memory backends

#### Scenario: Real mode flagged before mutation

- **WHEN** real credentials are configured and the user triggers an action that mutates an external system
- **THEN** the application indicates the action will affect real systems and requires explicit confirmation

### Requirement: jira-sync-core smoke panel

The application SHALL provide a panel that drives the jira-sync-core happy path: list mirrored
work items, simulate an inbound Jira webhook, submit a write-back, and view the outbox and the
fake-Jira comments — each refreshing the displayed state.

#### Scenario: View work items

- **WHEN** the user opens the work-items view (or triggers a backfill)
- **THEN** the panel lists the mirrored work items with key, status, and assignee

#### Scenario: Simulate a webhook

- **WHEN** the user submits a sample Jira issue-updated event from the panel
- **THEN** the application applies it through the webhook processor and the work-item view reflects the change

#### Scenario: Submit a write-back and observe delivery

- **WHEN** the user fills the write-back form for a work item and submits it
- **THEN** the panel queues the record and shows it move to Sent in the outbox view, with the resulting fake-Jira comment visible

### Requirement: Guided smoke-test run with pass/fail

The application SHALL provide a guided smoke run that executes a capability's happy-path
sequence step by step and reports a pass/fail result with detail for each step.

#### Scenario: Run the jira-sync-core smoke sequence

- **WHEN** the user starts the guided smoke run for jira-sync-core
- **THEN** the application executes the ordered steps (e.g. backfill → submit write-back → verify delivery) and shows each step's pass/fail status

#### Scenario: Failing step is surfaced

- **WHEN** a step in the guided run fails
- **THEN** the application marks that step failed, shows the error detail, and reports overall failure

#### Scenario: Successful run reports overall pass

- **WHEN** all steps in the guided run succeed
- **THEN** the application reports an overall pass result

### Requirement: Result and error visibility

The application SHALL display operation results and errors within the UI rather than crashing,
so a smoke test never leaves the user without feedback.

#### Scenario: Error shown without crashing

- **WHEN** an action raises an error (invalid input or a downstream failure)
- **THEN** the application shows the error message in the UI and remains usable
