## ADDED Requirements

### Requirement: Trigger conversation-to-work-item extraction

The system SHALL provide a channel command (`/triage`, alias `/actions`) that extracts candidate
action items either from the channel's recent captured conversation or from text pasted with the
command. When the channel is linked to a work item, the system SHALL resolve that link so created
issues can be back-linked.

#### Scenario: Trigger over captured conversation

- **WHEN** a user runs the command with no pasted text in a channel that has captured conversation
- **THEN** the system extracts candidate action items from the channel's recent captured messages

#### Scenario: Trigger over pasted notes

- **WHEN** a user runs the command with pasted meeting notes
- **THEN** the system extracts candidate action items from the pasted text instead of the captured
  conversation

#### Scenario: Claude unavailable degrades deterministically

- **WHEN** the Anthropic API key is not configured
- **THEN** extraction uses the fake fallback rather than failing the command

### Requirement: Extract candidate action items

The system SHALL use the Claude extraction seam to produce candidate action items, where each
candidate has a title, a description, and an issue type, gated on the Anthropic API key with the fake
fallback.

#### Scenario: Candidates have title, description, and issue type

- **WHEN** extraction runs over a conversation containing action items
- **THEN** each produced candidate has a title, a description, and an issue type

#### Scenario: No action items found

- **WHEN** extraction finds no action items in the conversation or notes
- **THEN** the system reports that no candidates were found and creates nothing

### Requirement: Confirm candidates via Block Kit cards

The system SHALL present each candidate action item as an interactive Block Kit card with Confirm and
Reject actions, routed through the existing interactivity endpoint, so a human approves each item
before any issue is created.

#### Scenario: Candidate presented for confirmation

- **WHEN** candidate action items are extracted
- **THEN** the system posts one interactive card per candidate showing its title, description, and
  issue type with Confirm and Reject buttons

#### Scenario: Reject creates nothing

- **WHEN** a user rejects a candidate card
- **THEN** the system creates no Jira issue for that candidate

### Requirement: Create confirmed action items as Jira issues

On confirmation, the system SHALL create the candidate as a new Jira issue using the Jira client's
create-issue capability, with the issue's project and issue type resolved from configuration, and
SHALL report the created issue's key back to the channel.

#### Scenario: Confirm creates a new issue

- **WHEN** a user confirms a candidate card
- **THEN** the system creates a new Jira issue with the candidate's title, description, and resolved
  issue type
- **AND** reports the new issue's key back to the channel

#### Scenario: Created issue uses configured project and type

- **WHEN** a candidate is confirmed
- **THEN** the new issue is created in the configured project with the configured issue type for that
  candidate's proposed type

#### Scenario: No project configured

- **WHEN** a candidate is confirmed but no project can be resolved for the channel and no default
  project is configured
- **THEN** the system does not create an issue and reports that no project is configured

### Requirement: Idempotent issue creation

The system SHALL create each confirmed candidate at most once by recording a deterministic dedupe key
derived from the candidate; re-confirming the same candidate SHALL return the already-created issue
rather than creating a duplicate.

#### Scenario: Re-confirm does not double-create

- **WHEN** a candidate that has already been created is confirmed again
- **THEN** the system creates no second issue and reports the existing issue's key

#### Scenario: Distinct candidates create distinct issues

- **WHEN** two different candidates from the same conversation are each confirmed
- **THEN** the system creates two distinct Jira issues

### Requirement: Back-link and attribute created issues

When the originating channel is linked to a work item, the system SHALL link the created issue back to
that work item and SHALL attribute the creation to the confirming Slack user and note the Slack
channel as the source.

#### Scenario: Back-link when channel maps to a work item

- **WHEN** a candidate is confirmed in a channel linked to a work item
- **THEN** the created issue is linked back to that originating work item

#### Scenario: Attribute to the confirming user

- **WHEN** a candidate is confirmed
- **THEN** the creation is attributed to the confirming Slack user and records the Slack channel as the
  source

#### Scenario: Create without back-link when channel has no mapping

- **WHEN** a candidate is confirmed in a channel that is not linked to a work item
- **THEN** the system creates the issue in the configured default project without a back-link
