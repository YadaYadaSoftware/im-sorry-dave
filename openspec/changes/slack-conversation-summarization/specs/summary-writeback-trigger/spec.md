## ADDED Requirements

### Requirement: Human confirmation before write-back

The system SHALL require human confirmation in Slack before any decision or answer candidate is written back to Jira.

#### Scenario: Candidate confirmed and written back

- **WHEN** a participant confirms a presented candidate in Slack
- **THEN** the system submits the candidate to Jira write-back and reports the result in the channel

#### Scenario: Candidate rejected

- **WHEN** a participant rejects or dismisses a presented candidate
- **THEN** the system does not write it to Jira and records the dismissal

#### Scenario: Candidate edited before confirmation

- **WHEN** a participant edits a candidate's text before confirming
- **THEN** the system writes back the edited content, attributed to the confirming user

### Requirement: Explicit summarization triggers

The system SHALL summarize a conversation only on an explicit trigger — the **`/post` slash command**,
or reacting to a message/thread with a configured emoji — and present the resulting candidates for
confirmation. Fully automatic (unsolicited) extraction is out of scope for this version.

#### Scenario: `/post` command invoked

- **WHEN** a user runs `/post` in a work-item channel
- **THEN** the system runs extraction over the conversation window since the last successful `/post`
  (see the window requirement) and presents the resulting candidates in the channel

#### Scenario: Summarize via emoji reaction

- **WHEN** a user reacts to a message or thread in a work-item channel with the configured summarize emoji
- **THEN** the system runs extraction over that thread/window and presents the resulting candidates

#### Scenario: `/post` used in unlinked channel

- **WHEN** `/post` (or another summarize trigger) fires in a channel not linked to a work item
- **THEN** the system responds that the channel is not linked and performs no write-back

### Requirement: Summarization window is the conversation since the last `/post`

The `/post` command SHALL summarize the messages posted in the channel **since the previous
successful `/post`** in that channel, so each `/post` covers only the new conversation and
consecutive posts do not re-summarize already-posted content. The system SHALL track a per-channel
cursor marking the point of the last successful `/post`, and SHALL advance it only when a `/post`
completes its write-back. On the first ever `/post` in a channel (no prior cursor), the window SHALL
be the channel's conversation from its creation.

#### Scenario: Window covers messages since the last post

- **WHEN** a user runs `/post` and a previous successful `/post` exists in the channel
- **THEN** the extraction window is the messages posted after the previous `/post`'s cursor, up to now

#### Scenario: First post covers the whole conversation

- **WHEN** a user runs `/post` in a channel that has never had a successful `/post`
- **THEN** the extraction window is the channel's conversation from its creation up to now

#### Scenario: Cursor advances only on success

- **WHEN** a `/post` completes its write-back successfully
- **THEN** the per-channel cursor advances to that point, so the next `/post` starts from there

#### Scenario: Cursor not advanced on no-op or failure

- **WHEN** a `/post` produces no confirmed write-back (nothing to post, all candidates rejected, or a
  failure)
- **THEN** the cursor does not advance, so the next `/post` still covers the same window

### Requirement: Write-back is idempotent and attributed

The system SHALL route confirmed candidates through the core write-back capability so the same candidate is not recorded twice and every record is attributed to its Slack source and confirming user.

#### Scenario: Re-confirming an already-written candidate

- **WHEN** a candidate that was already written back is confirmed again
- **THEN** the system updates the existing Jira record rather than creating a duplicate

#### Scenario: Write-back failure reported in Slack

- **WHEN** a confirmed write-back fails permanently
- **THEN** the system reports the failure in the originating channel so a human can act

### Requirement: Console commands drive summarization and candidate review

The console application SHALL provide commands to run extraction over a conversation, list extracted candidates, and confirm or reject a candidate for write-back.

#### Scenario: Summarize from the console

- **WHEN** the operator runs the summarize command for a work item or channel
- **THEN** the console runs extraction over the conversation window and prints the candidate decisions/answers/summary

#### Scenario: Confirm a candidate from the console

- **WHEN** the operator runs the candidate confirm command for a candidate id
- **THEN** the console submits the confirmed candidate to Jira write-back, honoring `--dry-run`
