# slack-channel-lifecycle Specification

## Purpose
TBD - created by archiving change slack-channel-provisioning. Update Purpose after archive.
## Requirements
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

The system SHALL invite relevant participants to a work item's channel where their Slack identity is
resolvable — a configured watcher list, the assignee, the item's creator (Jira reporter), and every
user @mentioned in the item's description or in a comment — skipping any participant whose Slack
identity cannot be resolved. Mention-based invites SHALL fire whenever the mention occurs (at channel
creation and afterward), for items that have a linked channel. When a user is invited because of a
mention, the system SHALL post a welcome message in the channel addressed to that user that includes
the **text of the mention** (the comment or description where they were @mentioned); when no welcome
message otherwise exists for that invite, this message serves as the welcome.

#### Scenario: Comment mention invites and welcomes with the mention text

- **WHEN** a comment on a work item with a linked channel @mentions a user whose Slack identity
  resolves
- **THEN** the system invites that user to the channel
- **AND** posts a welcome message addressed to them containing the text of the comment

#### Scenario: Description-edit mention invites and welcomes with the description text

- **WHEN** a work item's description is edited to @mention a new user whose Slack identity resolves
  and the item has a linked channel
- **THEN** the system invites that user and posts a welcome message addressed to them containing the
  description text

#### Scenario: No duplicate welcome at provision time

- **WHEN** a channel is auto-provisioned and the description @mentions users
- **THEN** the description is already posted as the channel's welcome and the system does not post a
  separate duplicate welcome for those mentions

#### Scenario: Creator and description mentions invited on creation

- **WHEN** a channel is created for a work item
- **THEN** the system invites the item's creator and every user @mentioned in the description whose
  Slack identity resolves, in addition to the assignee and the configured watcher list

#### Scenario: Mention on an item with no channel

- **WHEN** a mention occurs on a work item that has no linked channel
- **THEN** the system takes no invite or welcome action

#### Scenario: Unresolvable identity handled gracefully

- **WHEN** a participant's Slack identity cannot be resolved
- **THEN** the system continues without failing and records that the invite was skipped

### Requirement: Console commands drive channel provisioning

The console application SHALL provide commands to provision, archive, and unarchive a work item's Slack channel, invoking the same lifecycle services as the event-driven path.

#### Scenario: Provision a channel from the console

- **WHEN** the operator runs the slack provision command for a work-item key
- **THEN** the console creates (or reports the existing) channel for that work item

#### Scenario: Archive a channel from the console

- **WHEN** the operator runs the slack archive command for a work-item key
- **THEN** the console archives the linked channel, honoring `--dry-run`

### Requirement: Provision a channel automatically on work-item creation

The system SHALL create a Slack channel for a work item **automatically when the item is created**
(the inbound `jira:issue_created` event), for configured discussion-worthy issue types, without an
explicit request. Explicit provisioning (console/slash command) SHALL remain available and
idempotent. Provisioned channels SHALL be public by default and named `<jira-key>-<short-summary-slug>`
(normalized to Slack's rules), and the channel SHALL be seeded with two welcome messages: a **pinned**
header containing the work item's title and a link to the Jira issue, and a second message containing
the work item's description.

#### Scenario: Channel created on work-item creation

- **WHEN** a new eligible work item is created
- **THEN** the system automatically creates a public Slack channel named `<jira-key>-<short-summary-slug>`
  and records the channel ↔ work-item link

#### Scenario: Welcome messages seed the channel

- **WHEN** a channel is created for a work item
- **THEN** the system posts a header message with the work item's title and a link to the Jira issue
  and **pins** it, and posts a second message containing the work item's description

#### Scenario: Out-of-scope issue types get no channel

- **WHEN** a work item whose issue type is outside the configured scope is created
- **THEN** the system does not create a Slack channel for it

#### Scenario: Explicit provisioning remains available and idempotent

- **WHEN** an operator runs the provision command for a work item that already has a (e.g.
  auto-provisioned) channel
- **THEN** the system reports the existing channel and does not create a duplicate

#### Scenario: Channel name normalized to Slack rules

- **WHEN** the channel name is derived (e.g., MDP-7 "Build Slack Channel")
- **THEN** it is lowercased with non-alphanumeric runs reduced to single hyphens (e.g.,
  `mdp-7-build-slack-channel`), the work-item key preserved, and the summary slug truncated within
  Slack's length limit

#### Scenario: Channel name collision resolved deterministically

- **WHEN** the derived channel name already exists for a different work item
- **THEN** the system applies a deterministic disambiguation suffix and records the actual channel created

