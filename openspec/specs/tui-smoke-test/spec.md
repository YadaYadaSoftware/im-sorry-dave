# tui-smoke-test Specification

## Purpose
TBD - created by archiving change tui-smoke-test. Update Purpose after archive.
## Requirements
### Requirement: Interactive Terminal.Gui shell

The application SHALL present a keyboard-navigable Terminal.Gui interface with a top-level menu
of capability panels and a status bar showing the connected API and key actions.

#### Scenario: Launch shows the capability menu

- **WHEN** the application starts
- **THEN** it renders a Terminal.Gui window with a menu listing the available capability panels and a status bar

#### Scenario: Navigate between panels

- **WHEN** the user selects a capability from the menu
- **THEN** the application shows that capability's panel and keeps the menu/status bar available

#### Scenario: Quit cleanly

- **WHEN** the user invokes the quit action
- **THEN** the application shuts down the Terminal.Gui loop and exits with code 0

### Requirement: Connected API is visible

The application SHALL operate against the API resolved from configuration and SHALL display the
API base address it is connected to, so the user knows which backend (and therefore which Jira)
their actions reach. The console does not itself choose fake vs. real — it reflects whatever the
API is configured for.

#### Scenario: API address shown

- **WHEN** the application starts
- **THEN** the status bar shows the API base address the console is connected to

#### Scenario: Backend determined by the API

- **WHEN** the user performs an action
- **THEN** the result reflects the API's configured backend (fake or real Jira), not a mode chosen by the console

### Requirement: jira-sync-core smoke panel

The application SHALL provide a panel that drives the jira-sync-core happy path through the API:
list work items, select one, simulate an inbound webhook, submit a write-back against the
selected item, and view the outbox and (when the API uses the fake backend) the Jira comments —
refreshing the displayed state after each action.

#### Scenario: View work items

- **WHEN** the user triggers a backfill or refresh
- **THEN** the panel lists the work items from the API with key, status, and assignee

#### Scenario: Actions target the selected work item

- **WHEN** the user selects a work item in the list and submits a write-back (or simulates a webhook)
- **THEN** the action targets the selected work item, and the panel reports when no item is selected

#### Scenario: Submit a write-back and observe delivery

- **WHEN** the user submits a write-back for the selected item
- **THEN** the panel posts it through the API, drains delivery, and shows the record reach Sent in the outbox view

### Requirement: Guided smoke-test run with pass/fail

The application SHALL provide a guided smoke run that executes a capability's happy-path sequence
step by step (through the API) and reports a pass/fail result with detail for each step.

#### Scenario: Run the jira-sync-core smoke sequence

- **WHEN** the user starts the guided smoke run for jira-sync-core
- **THEN** the application executes the ordered steps (backfill → submit write-back → deliver → verify) and shows each step's pass/fail status

#### Scenario: Failing step is surfaced

- **WHEN** a step in the guided run fails
- **THEN** the application marks that step failed, shows the error detail, and reports overall failure

#### Scenario: Successful run reports overall pass

- **WHEN** all steps in the guided run succeed
- **THEN** the application reports an overall pass result

### Requirement: Result and error visibility

The application SHALL display operation results and errors within the UI rather than crashing,
including when the API is unreachable, so a smoke test never leaves the user without feedback.

#### Scenario: Error shown without crashing

- **WHEN** an action raises an error (invalid input, no selection, or the API being unreachable)
- **THEN** the application shows the error message in the UI and remains usable

