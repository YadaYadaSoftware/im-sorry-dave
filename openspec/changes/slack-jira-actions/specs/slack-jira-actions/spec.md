## ADDED Requirements

### Requirement: Slash commands act on the linked work item

The system SHALL accept signature-verified slash commands `/assign @user`, `/status <name>`, and
`/comment <text>` in a work-item channel, resolve the channel to its linked Jira work item via
`IMappingStore.ResolveByResourceAsync(ResourceType.SlackChannel, channelId)`, apply the action to
Jira, and report the result in the channel; commands issued in a channel with no linked work item
SHALL take no Jira action and reply that the channel is not linked.

#### Scenario: Assign resolves identity and updates the assignee

- **WHEN** a user runs `/assign @user` in a channel linked to a work item and the mentioned user's
  Jira identity resolves
- **THEN** the system sets that user as the work item's assignee via `IJiraClient`
- **AND** posts the result (the new assignee) in the channel

#### Scenario: Status transitions the work item

- **WHEN** a user runs `/status <name>` in a linked channel and `<name>` matches an available Jira
  transition
- **THEN** the system transitions the work item to that status via `IJiraClient` and reports the new
  status in the channel
- **AND** when `<name>` matches no available transition, the system replies with the valid
  transition names and does not change the status

#### Scenario: Comment is written back through the idempotent outbox

- **WHEN** a user runs `/comment <text>` in a linked channel
- **THEN** the system submits the comment through `IWriteBackService.SubmitAsync` (idempotent on
  `WorkItemKey`+`RecordIdentity`) so it is added to the Jira work item carrying the
  `[managed-record:…]` marker
- **AND** reports in the channel that the comment was added

#### Scenario: Command in an unlinked channel takes no action

- **WHEN** any of these commands is run in a channel that has no linked work item
- **THEN** the system applies no Jira change and replies that the channel is not linked

### Requirement: Identity resolution for assignment

The system SHALL resolve the `@user` argument of `/assign` to a Jira accountId using the platform's
identity resolution before applying the assignment, and SHALL NOT change the assignee when the
identity cannot be resolved.

#### Scenario: Unresolvable user does not change the assignee

- **WHEN** `/assign @user` is run and the mentioned user's Jira identity cannot be resolved
- **THEN** the system does not change the work item's assignee
- **AND** replies that the user could not be resolved

### Requirement: Reflect inbound Jira comments into the linked channel

The system SHALL, when a `comment_created` webhook arrives for a work item that has a linked Slack
channel, post the comment's author and text to that channel so the channel mirrors the Jira
conversation; when the work item has no linked channel the system SHALL take no action.

#### Scenario: Human Jira comment is mirrored to the channel

- **WHEN** a `comment_created` webhook arrives for a work item with a linked channel and the comment
  was not authored by the platform
- **THEN** the system posts the comment (author and text) to the linked channel

#### Scenario: Comment on an item with no channel is ignored

- **WHEN** a `comment_created` webhook arrives for a work item that has no linked channel
- **THEN** the system posts nothing

### Requirement: Managed-record comments are not reflected

The system SHALL skip reflecting any comment whose body carries the `[managed-record:…]` marker, so
platform-authored comments (including those produced by `/comment`) are not echoed back into the
channel and no feedback loop forms.

#### Scenario: Platform-authored comment does not echo back

- **WHEN** a `comment_created` webhook arrives for a comment whose body contains the
  `[managed-record:…]` marker on a work item with a linked channel
- **THEN** the system does not post that comment to the channel
