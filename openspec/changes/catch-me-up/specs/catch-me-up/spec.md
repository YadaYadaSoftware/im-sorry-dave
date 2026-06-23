## ADDED Requirements

### Requirement: Per-user catch-up digest

The system SHALL provide a `/catchup` slash command on the signature-verified slash endpoint
(`/slack/commands`) that, when invoked by a user in a work-item channel or a DM, produces an **ephemeral**
digest — visible only to that user — of what changed on the associated work item since **that user's**
personal read cursor, and SHALL advance the user's cursor when a digest is delivered. The read cursor
SHALL be per-`(user, channel/work-item)` and SHALL be distinct from the shared per-channel `PostCursor`.
The digest SHALL be drawn from already-mirrored seams: the `WorkItem` (status/assignee changes), new
Jira comments, confirmed decisions (`WriteBackRecord`), and notable conversation (`CapturedMessage`).

#### Scenario: Digest of the delta since the user's cursor

- **WHEN** a user runs `/catchup` in a channel linked to a work item and there are changes since that
  user's read cursor
- **THEN** the system resolves the work item via `IMappingStore`, assembles the delta (status/assignee
  changes, new Jira comments, confirmed `WriteBackRecord` decisions, and notable `CapturedMessage`
  conversation) since the user's cursor, and replies with an ephemeral digest to that user only
- **AND** advances that user's read cursor to the high-water mark of the included delta

#### Scenario: Claude summarizes when a key is configured

- **WHEN** `Anthropic:ApiKey` is configured and a `/catchup` delta is non-empty
- **THEN** the system summarizes the delta via Claude into a concise narrative grouped by category and
  returns it ephemerally

#### Scenario: Graceful fallback without an Anthropic key

- **WHEN** `Anthropic:ApiKey` is not configured and a `/catchup` delta is non-empty
- **THEN** the system returns a deterministic, grounded listing of the same delta (status/assignee
  changes, new comments, confirmed decisions, notable messages) without fabricating content

#### Scenario: Nothing new since last catch-up

- **WHEN** a user runs `/catchup` and there are no changes since that user's read cursor
- **THEN** the system replies ephemerally that the user is already caught up and does not advance the
  user's cursor

#### Scenario: Personal cursor is distinct from the shared post cursor

- **WHEN** one user runs `/catchup` and is caught up
- **THEN** the shared per-channel `PostCursor` is unchanged and other users' read cursors are unchanged,
  so each user's catch-up reflects only their own last read

#### Scenario: Invocation with no linked work item

- **WHEN** a user runs `/catchup` in a DM or a channel that `IMappingStore` does not map to a work item
- **THEN** the system replies ephemerally that no work item is linked here and takes no cursor action

#### Scenario: First-time catch-up with no existing cursor

- **WHEN** a user runs `/catchup` for the first time on a work item and has no read cursor yet
- **THEN** the system uses a bounded baseline (e.g. the work item's/channel's creation point) as the
  starting cursor so the digest is concise rather than unbounded, then advances the cursor
