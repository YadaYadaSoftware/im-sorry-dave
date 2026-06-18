## ADDED Requirements

### Requirement: Human confirmation before write-back

The system SHALL require human confirmation in Slack before any decision or answer candidate is written back to Jira.

#### Scenario: Candidate confirmed and written back

- **WHEN** a participant confirms a presented candidate in Slack
- **THEN** the system submits the candidate to Jira write-back and reports the result in the channel

#### Scenario: Candidate rejected

- **WHEN** a participant rejects or dismisses a presented candidate
- **THEN** the system does not write it to Jira and records the dismissal

#### Scenario: Candidate edited before confirmation

- **WHEN** a participant edits a candidate's text before confirming
- **THEN** the system writes back the edited content, attributed to the confirming user

### Requirement: On-demand summarization command

The system SHALL provide a Slack command that summarizes the current thread or channel on demand and presents candidates for confirmation.

#### Scenario: Manual summarize invoked

- **WHEN** a user invokes the summarize command in a work-item channel
- **THEN** the system runs extraction over the relevant conversation window and presents the resulting candidates in the channel

#### Scenario: Command used in unlinked channel

- **WHEN** the summarize command is invoked in a channel not linked to a work item
- **THEN** the system responds that the channel is not linked and performs no write-back

### Requirement: Write-back is idempotent and attributed

The system SHALL route confirmed candidates through the core write-back capability so the same candidate is not recorded twice and every record is attributed to its Slack source and confirming user.

#### Scenario: Re-confirming an already-written candidate

- **WHEN** a candidate that was already written back is confirmed again
- **THEN** the system updates the existing Jira record rather than creating a duplicate

#### Scenario: Write-back failure reported in Slack

- **WHEN** a confirmed write-back fails permanently
- **THEN** the system reports the failure in the originating channel so a human can act
