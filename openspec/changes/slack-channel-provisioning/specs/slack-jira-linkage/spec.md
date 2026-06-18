## ADDED Requirements

### Requirement: Durable channel ↔ work-item link

The system SHALL record a unique, durable association between a Slack channel and its Jira work item using the core mapping store, so that any message in the channel can be attributed to exactly one work item.

#### Scenario: Link recorded on provisioning

- **WHEN** a channel is created for a work item
- **THEN** the system records the channel ID ↔ work item key association in the mapping store

#### Scenario: Resolve work item from channel

- **WHEN** the platform needs the work item for a given Slack channel
- **THEN** the mapping store returns the linked work item, or indicates none

#### Scenario: One channel maps to one work item

- **WHEN** an attempt is made to link a channel already linked to a different work item
- **THEN** the system rejects the link as a conflict

### Requirement: Reflect work-item context into the channel

The system SHALL keep the channel's displayed context (topic and/or purpose) aligned with the work item's current status and assignee as Jira changes.

#### Scenario: Status change reflected

- **WHEN** a linked work item's status changes in Jira
- **THEN** the system updates the channel topic and/or posts a status-change message reflecting the new status

#### Scenario: Assignee change reflected

- **WHEN** a linked work item's assignee changes in Jira
- **THEN** the system updates the channel context to show the new assignee

#### Scenario: Jira link available in channel

- **WHEN** a member views the channel
- **THEN** the channel exposes a link back to the Jira work item (via topic, purpose, or pinned message)
