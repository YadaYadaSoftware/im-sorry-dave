## ADDED Requirements

### Requirement: Link OpenSpec changes to work items

The system SHALL record an association between an OpenSpec change (identified by its change name) and one or more Jira work items in the core mapping store, resolvable in both directions.

#### Scenario: Change linked to work item

- **WHEN** an OpenSpec change is associated with a Jira work item
- **THEN** the system records the change-name ↔ work-item association in the mapping store

#### Scenario: Resolve change from work item

- **WHEN** the platform needs the OpenSpec change linked to a work item (or vice versa)
- **THEN** the mapping store returns the association, or indicates none

### Requirement: Surface OpenSpec status onto Jira and Slack

The system SHALL surface the OpenSpec change's artifact and task status onto the linked Jira issue and its Slack channel.

#### Scenario: Status reflected to Jira

- **WHEN** an OpenSpec change's status changes (e.g., an artifact becomes complete or the change becomes apply-ready)
- **THEN** the system updates the linked Jira issue with the current OpenSpec status

#### Scenario: Status reflected to Slack

- **WHEN** an OpenSpec change linked to a work item changes status
- **THEN** the system posts the status update to the work item's Slack channel

#### Scenario: Unlinked change ignored

- **WHEN** a status change occurs for an OpenSpec change not linked to any work item
- **THEN** the system performs no Jira or Slack update

### Requirement: Read OpenSpec status reliably

The system SHALL determine OpenSpec change status from the authoritative OpenSpec source (the `openspec` CLI status/show output or the change artifacts).

#### Scenario: Status obtained from OpenSpec

- **WHEN** the system needs a change's artifact/task status
- **THEN** it reads the status from the OpenSpec CLI/artifacts rather than inferring it from Jira
