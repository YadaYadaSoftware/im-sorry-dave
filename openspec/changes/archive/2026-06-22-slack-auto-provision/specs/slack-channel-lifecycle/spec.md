## RENAMED Requirements

- FROM: `### Requirement: Provision a channel lazily on explicit request`
- TO: `### Requirement: Provision a channel automatically on work-item creation`

## MODIFIED Requirements

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

### Requirement: Channel membership management

The system SHALL invite relevant participants to a work item's channel where their Slack identity is
resolvable — a configured watcher list, the assignee, the item's **creator** (Jira reporter), and
every user **@mentioned in the item's description** — skipping any participant whose Slack identity
cannot be resolved.

#### Scenario: Creator and mentioned users invited on creation

- **WHEN** a channel is created for a work item
- **THEN** the system invites the item's creator and every user @mentioned in the description whose
  Slack identity resolves, in addition to the assignee and the configured watcher list

#### Scenario: Assignee invited when assignment changes

- **WHEN** a channel exists and the assignee changes and the new assignee's Slack identity is known
- **THEN** the system invites the assignee to the channel

#### Scenario: Unresolvable identity handled gracefully

- **WHEN** a participant's Slack identity cannot be resolved
- **THEN** the system continues without failing and records that the invite was skipped
