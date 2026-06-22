## MODIFIED Requirements

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
