## ADDED Requirements

### Requirement: Provision a channel per work item

The system SHALL create a Slack channel for each tracked Jira work item that does not yet have one, deriving the channel name deterministically from the work item key and seeding it with the work item's context.

#### Scenario: Channel created for new work item

- **WHEN** a work item becomes tracked and has no linked Slack channel
- **THEN** the system creates a Slack channel whose name incorporates the work item key (normalized to Slack naming rules)
- **AND** posts an initial context message containing the key, summary, type, status, assignee, and a link to the Jira issue

#### Scenario: Channel name collision resolved deterministically

- **WHEN** the derived channel name already exists for a different work item
- **THEN** the system applies a deterministic disambiguation suffix and records the actual channel created

#### Scenario: No duplicate channel created

- **WHEN** a work item already has a linked, non-archived Slack channel
- **THEN** the system does not create another channel for it

### Requirement: Channel lifecycle follows work-item state

The system SHALL archive a work item's channel when the item reaches a terminal/closed state and SHALL re-activate (unarchive) it if the item reopens.

#### Scenario: Channel archived on completion

- **WHEN** a tracked work item transitions to a completed/closed status
- **THEN** the system archives the linked Slack channel rather than deleting it
- **AND** posts a closing summary message before archiving

#### Scenario: Channel re-activated on reopen

- **WHEN** a closed work item with an archived channel is reopened in Jira
- **THEN** the system unarchives the linked channel and posts a re-activation notice

### Requirement: Channel membership management

The system SHALL ensure relevant participants (e.g., assignee, reporter) have access to the work item's channel where their Slack identity is resolvable.

#### Scenario: Assignee invited to channel

- **WHEN** a channel is created or the assignee changes and the assignee's Slack identity is known
- **THEN** the system invites the assignee to the channel

#### Scenario: Unresolvable identity handled gracefully

- **WHEN** a participant's Slack identity cannot be resolved
- **THEN** the system continues provisioning without failing and records that the invite was skipped

### Requirement: Console commands drive channel provisioning

The console application SHALL provide commands to provision, archive, and unarchive a work item's Slack channel, invoking the same lifecycle services as the event-driven path.

#### Scenario: Provision a channel from the console

- **WHEN** the operator runs the slack provision command for a work-item key
- **THEN** the console creates (or reports the existing) channel for that work item

#### Scenario: Archive a channel from the console

- **WHEN** the operator runs the slack archive command for a work-item key
- **THEN** the console archives the linked channel, honoring `--dry-run`
