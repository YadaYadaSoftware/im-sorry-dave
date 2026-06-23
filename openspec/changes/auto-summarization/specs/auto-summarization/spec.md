## ADDED Requirements

### Requirement: Idle-timeout summarization trigger

The system SHALL provide an optional, off-by-default background trigger that, when enabled, summarizes
a linked work-item channel after a configured period of inactivity. The trigger SHALL fire only for a
channel whose newest non-deleted captured message is older than the configured idle timeout AND that
has conversation captured after the channel's post cursor, and it SHALL run the same summarize →
candidate-card flow as the explicit `/post` command, producing candidate cards that require human
confirmation before any write-back.

#### Scenario: Idle channel with new conversation is summarized

- **WHEN** the idle trigger is enabled and a linked channel has had no new message for longer than the
  configured idle timeout and has captured messages after its post cursor
- **THEN** the system summarizes the since-cursor window and posts interactive candidate cards to the
  channel for human confirmation

#### Scenario: Active channel is not summarized

- **WHEN** a linked channel has received a message more recently than the configured idle timeout
- **THEN** the idle sweep skips that channel and takes no summarization action

#### Scenario: Idle channel with nothing new is skipped

- **WHEN** a linked channel is idle but has no captured messages after its post cursor
- **THEN** the system does not run summarization for that channel

#### Scenario: Idle trigger disabled by default

- **WHEN** the idle trigger has not been explicitly enabled in configuration
- **THEN** the background sweep performs no automatic summarization

### Requirement: Reaction-cue summarization trigger

The system SHALL provide an optional, off-by-default trigger that summarizes a linked work-item
channel when a message in that channel is reacted to with the configured cue emoji. The trigger SHALL
be handled on the signature-verified `/slack/events` endpoint via the `reaction_added` event, SHALL
acknowledge within Slack's time limit and run the extraction work in the background, and SHALL produce
candidate cards that require human confirmation before any write-back. Receiving the reaction event
requires the Slack app to subscribe to `reaction_added` and hold the `reactions:read` scope.

#### Scenario: Configured reaction on a linked channel summarizes

- **WHEN** the reaction trigger is enabled and a user adds the configured cue emoji to a message in a
  channel linked to a work item
- **THEN** the system summarizes that channel's since-cursor window and posts interactive candidate
  cards for human confirmation

#### Scenario: Non-cue emoji is ignored

- **WHEN** a user reacts to a message with an emoji other than the configured cue emoji
- **THEN** the system takes no summarization action

#### Scenario: Reaction on an unlinked channel is ignored

- **WHEN** the cue emoji is added to a message in a channel not linked to a work item
- **THEN** the system takes no summarization action

#### Scenario: Reaction trigger disabled by default

- **WHEN** the reaction trigger has not been explicitly enabled in configuration
- **THEN** the `/slack/events` endpoint takes no summarization action on `reaction_added` events

### Requirement: Human confirmation gates automatic write-back

Every automatic trigger SHALL produce candidate cards through the existing confirm/reject loop and
SHALL NOT write anything back to Jira without an explicit human confirmation. No automatic trigger
SHALL ever perform an unsolicited write-back.

#### Scenario: Auto-generated candidates require confirmation

- **WHEN** an idle or reaction trigger produces summary candidates
- **THEN** the candidates are posted as interactive cards and nothing is written to Jira until a human
  confirms a candidate

#### Scenario: Rejected auto candidate is not written back

- **WHEN** a human rejects a candidate produced by an automatic trigger
- **THEN** no write-back occurs and the channel's post cursor is unchanged

### Requirement: Throttling and de-duplication for automatic triggers

Automatic triggers SHALL be cost-bounded by a per-channel cooldown that prevents a channel from being
auto-summarized again within the configured cooldown window, and SHALL de-duplicate against the
channel's post cursor so that an automatic run and a manual `/post` (or two automatic runs) never
summarize or write back the same window twice. The explicit `/post` command SHALL bypass the cooldown.

#### Scenario: Cooldown suppresses a rapid second auto-trigger

- **WHEN** a channel was auto-summarized and another automatic trigger fires for it within the
  configured cooldown window
- **THEN** the system does not run a second extraction for that channel until the cooldown elapses

#### Scenario: Auto and manual do not double-summarize the same window

- **WHEN** an automatic trigger and a manual `/post` both target a channel
- **THEN** both summarize only conversation after the channel's post cursor, so the same window is not
  summarized or written back twice

#### Scenario: Explicit /post bypasses the cooldown

- **WHEN** an operator runs `/post` on a channel that is within its auto-trigger cooldown window
- **THEN** the command runs the summarization normally

### Requirement: Configuration to enable and scope automatic triggers

The system SHALL expose configuration to enable each automatic trigger independently (both default
disabled), set the idle timeout, sweep interval, per-channel cooldown, and cue emoji, and to scope
which channels or issue types are eligible for automatic summarization.

#### Scenario: Triggers default off

- **WHEN** no automatic-summarization configuration is provided
- **THEN** neither the idle nor the reaction trigger fires and behavior matches explicit-only v1

#### Scenario: Scope limits eligible channels

- **WHEN** automatic triggers are configured to apply only to specific issue types or channels
- **THEN** only channels matching that scope are eligible for automatic summarization
