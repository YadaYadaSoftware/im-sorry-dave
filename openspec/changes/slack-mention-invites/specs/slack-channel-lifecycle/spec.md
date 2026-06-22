## MODIFIED Requirements

### Requirement: Channel membership management

The system SHALL invite relevant participants to a work item's channel where their Slack identity is
resolvable — a configured watcher list, the assignee, the item's creator (Jira reporter), and every
user **@mentioned in the item's description or in a comment** — skipping any participant whose Slack
identity cannot be resolved. Mention-based invites SHALL fire **whenever the mention occurs** (at
channel creation and afterward), for items that have a linked channel.

#### Scenario: Creator and description mentions invited on creation

- **WHEN** a channel is created for a work item
- **THEN** the system invites the item's creator and every user @mentioned in the description whose
  Slack identity resolves, in addition to the assignee and the configured watcher list

#### Scenario: Comment mention invites after creation

- **WHEN** a comment is added to a work item that has a linked channel and the comment @mentions a
  user whose Slack identity resolves
- **THEN** the system invites that user to the channel

#### Scenario: Description mention added after creation

- **WHEN** a work item's description is edited to @mention a new user whose Slack identity resolves
  and the item has a linked channel
- **THEN** the system invites that user to the channel

#### Scenario: Mention on an item with no channel

- **WHEN** a mention occurs on a work item that has no linked channel
- **THEN** the system takes no invite action

#### Scenario: Assignee invited when assignment changes

- **WHEN** a channel exists and the assignee changes and the new assignee's Slack identity is known
- **THEN** the system invites the assignee to the channel

#### Scenario: Unresolvable identity handled gracefully

- **WHEN** a participant's Slack identity cannot be resolved
- **THEN** the system continues without failing and records that the invite was skipped
