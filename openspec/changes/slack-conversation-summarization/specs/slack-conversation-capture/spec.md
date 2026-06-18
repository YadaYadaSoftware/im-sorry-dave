## ADDED Requirements

### Requirement: Capture messages from work-item channels

The system SHALL receive Slack message events from channels linked to work items and store them as a transcript associated with the corresponding work item, preserving author, timestamp, thread relationship, and edits.

#### Scenario: Message in linked channel captured

- **WHEN** a message is posted in a channel linked to a work item
- **THEN** the system resolves the work item via the channel link and stores the message in that work item's transcript with author and timestamp

#### Scenario: Message in unlinked channel ignored

- **WHEN** a message event arrives for a channel not linked to any work item
- **THEN** the system ignores it without error

#### Scenario: Edited or deleted message reflected

- **WHEN** a captured message is edited or deleted in Slack
- **THEN** the system updates the stored transcript to reflect the change

### Requirement: Thread fidelity

The system SHALL preserve thread structure so that extraction can operate on a coherent conversation unit (a thread or a channel window).

#### Scenario: Threaded reply linked to parent

- **WHEN** a message is a threaded reply
- **THEN** the stored transcript records its parent so the thread can be reconstructed

### Requirement: Event authenticity and de-duplication

The system SHALL verify Slack event authenticity and SHALL process each event at most once.

#### Scenario: Unverified event rejected

- **WHEN** a Slack event request fails signature verification
- **THEN** the system rejects it and stores nothing

#### Scenario: Duplicate delivery ignored

- **WHEN** Slack redelivers an event already processed
- **THEN** the system does not create a duplicate transcript entry
