## ADDED Requirements

### Requirement: Configurable PR-event to status mapping

The system SHALL transition a linked work item's Jira status based on pull request lifecycle events using a configurable mapping from event to target status.

#### Scenario: Opening a PR advances status

- **WHEN** a linked pull request is opened and the mapping defines a target status for "PR opened"
- **THEN** the system transitions the work item to the mapped status (e.g., In Review / In Progress)

#### Scenario: Merging a PR advances status

- **WHEN** a linked pull request is merged and the mapping defines a target status for "PR merged"
- **THEN** the system transitions the work item to the mapped status (e.g., Done)

#### Scenario: Event with no mapping

- **WHEN** a PR lifecycle event has no configured target status
- **THEN** the system performs no transition

### Requirement: Respect Jira workflow validity

The system SHALL only perform transitions that are valid for the work item's current Jira workflow state and SHALL handle invalid transitions without error.

#### Scenario: Invalid transition skipped

- **WHEN** the mapped target status is not a valid transition from the work item's current status
- **THEN** the system does not force the transition and records that it was skipped

#### Scenario: Already in target status

- **WHEN** the work item is already in the mapped target status
- **THEN** the system performs no transition

### Requirement: Transitions are idempotent and attributed

The system SHALL avoid redundant transitions and SHALL attribute automated transitions to the platform with a reference to the triggering pull request.

#### Scenario: Duplicate event causes no extra transition

- **WHEN** a PR event that already drove a transition is redelivered
- **THEN** the system does not perform the transition again

#### Scenario: Transition attributed to PR

- **WHEN** the system transitions a work item from a PR event
- **THEN** the transition is recorded with a reference to the triggering pull request

### Requirement: Console commands drive status transitions

The console application SHALL provide a command to apply the configured PR-event status transition for a linked work item, supporting `--dry-run`.

#### Scenario: Apply a transition from the console

- **WHEN** the operator runs the github apply-status command for a linked pull request and event
- **THEN** the console performs the mapped, workflow-valid transition, or reports it would under `--dry-run`

#### Scenario: Invalid transition reported

- **WHEN** the mapped target status is not a valid transition for the work item's current status
- **THEN** the console reports that the transition was skipped
