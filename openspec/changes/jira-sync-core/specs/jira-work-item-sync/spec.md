## ADDED Requirements

### Requirement: Canonical work-item model

The system SHALL maintain an internal representation of each tracked Jira work item that includes at minimum its Jira key, project, issue type, status, assignee, reporter, summary, description, labels, and a collection of linked external resources (Slack channels, GitHub pull requests, OpenSpec changes). The Jira value SHALL be treated as authoritative for any field also editable in Jira.

#### Scenario: Work item mirrored on first observation

- **WHEN** the service first observes a Jira work item (via webhook or reconciliation)
- **THEN** it creates an internal record populated from the Jira issue fields
- **AND** records the Jira `updated` timestamp as the last-synced version marker

#### Scenario: Authoritative fields reflect Jira

- **WHEN** the internal record and Jira disagree on an authoritative field (status, assignee, summary, description)
- **THEN** the system overwrites the internal value with the Jira value during sync
- **AND** never writes a conflicting value for that field back to Jira

### Requirement: Near-real-time ingestion via webhooks

The system SHALL expose an HTTPS endpoint that accepts Jira webhook events for issue created, issue updated, issue deleted, and comment created, and SHALL update the internal store to reflect each event.

#### Scenario: Issue updated webhook applied

- **WHEN** a Jira `jira:issue_updated` webhook is received for a tracked issue
- **THEN** the system updates the internal record to match the event payload
- **AND** acknowledges the webhook with a 2xx response within the provider timeout

#### Scenario: Webhook signature rejected

- **WHEN** a webhook request arrives without a valid configured secret/signature
- **THEN** the system rejects it with HTTP 401 and does not mutate any state

#### Scenario: Out-of-order or stale event ignored

- **WHEN** a webhook event carries an `updated` timestamp older than the last-synced version marker for that issue
- **THEN** the system discards the event without overwriting newer data

### Requirement: Reconciliation sweep

The system SHALL periodically reconcile tracked work items against Jira to recover from missed or dropped webhook events.

#### Scenario: Missed update recovered by reconciliation

- **WHEN** a scheduled reconciliation runs and finds a Jira issue whose `updated` timestamp is newer than the internal record
- **THEN** the system refreshes the internal record from Jira
- **AND** records the new version marker

#### Scenario: Deleted issue reconciled

- **WHEN** reconciliation finds a tracked issue that no longer exists in Jira
- **THEN** the system marks the internal record as deleted and retains it for audit

### Requirement: Work-item to external-resource mapping

The system SHALL provide a mapping store that associates a work item with its external resources and SHALL allow other capabilities to look up a work item by any associated resource identifier.

#### Scenario: Resolve work item from external resource

- **WHEN** another capability requests the work item linked to a given external resource identifier (e.g., a Slack channel ID)
- **THEN** the system returns the associated work item, or indicates none is linked

#### Scenario: Mapping is unique per resource

- **WHEN** a resource is linked to a work item that is already linked to a different work item
- **THEN** the system rejects the duplicate link and surfaces a conflict
