# jira-decision-writeback Specification

## Purpose
TBD - created by archiving change jira-sync-core. Update Purpose after archive.
## Requirements
### Requirement: Structured write-back to Jira

The system SHALL write decisions, answered questions, and conversation summaries back to a work item as Jira comments and/or designated fields, using a structured, recognizable format that identifies the record type and its source.

#### Scenario: Decision written as attributed comment

- **WHEN** a confirmed decision is submitted for write-back to a work item
- **THEN** the system posts a Jira comment containing the decision text, the originating source (e.g., Slack channel/thread link), and a machine-readable marker identifying it as a managed decision record

#### Scenario: Answer recorded against a question

- **WHEN** an answer is submitted for a previously recorded question on a work item
- **THEN** the system records the answer linked to that question in Jira and marks the question as answered

### Requirement: Idempotent write-back

The system SHALL ensure that submitting the same logical record more than once does not create duplicate Jira content.

#### Scenario: Duplicate submission deduplicated

- **WHEN** a write-back is submitted with a record identity that has already been written to the same work item
- **THEN** the system updates the existing Jira content (or no-ops) rather than creating a new comment

#### Scenario: Edited record updates in place

- **WHEN** a previously written record is resubmitted with changed content under the same identity
- **THEN** the system edits the existing Jira comment/field to reflect the new content

### Requirement: Attribution and traceability

Every managed write-back SHALL be attributed to its human source and conversation of origin so that Jira readers can trace a decision back to its discussion.

#### Scenario: Source link present

- **WHEN** any decision or answer is written back
- **THEN** the Jira content includes a link or reference to the originating conversation and the responsible person

### Requirement: Write-back failure handling

The system SHALL handle Jira write failures without losing the record and SHALL retry transient failures.

#### Scenario: Transient Jira error retried

- **WHEN** a write-back fails due to a transient Jira error (timeout, 5xx, rate limit)
- **THEN** the system queues the record for retry with backoff and does not drop it

#### Scenario: Permanent failure surfaced

- **WHEN** a write-back fails permanently (e.g., issue does not exist, permission denied)
- **THEN** the system records the failure and surfaces it to operators rather than silently discarding the record

### Requirement: Console commands drive write-back

The console application SHALL provide commands to submit a decision/answer/summary for write-back, list outbox records with their delivery status, and retry a failed record.

#### Scenario: Submit a write-back from the console

- **WHEN** the operator runs the writeback submit command with a work-item key, record identity, kind, and content
- **THEN** the console queues the record idempotently and reports its status

#### Scenario: Inspect the outbox

- **WHEN** the operator runs the writeback list command
- **THEN** the console prints outbox records with their status, attempts, and last error

#### Scenario: Retry a failed record

- **WHEN** the operator runs the writeback retry command for a failed record
- **THEN** the console re-queues it for delivery and reports the new status

