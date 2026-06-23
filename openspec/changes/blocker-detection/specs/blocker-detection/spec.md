## ADDED Requirements

### Requirement: Blocker and risk detection from captured conversation

The system SHALL classify a bounded window of a work item's captured Slack conversation
(`CapturedMessage`) using Claude (`IDecisionExtractor`, gated on `Anthropic:ApiKey` with the fake
fallback) to determine whether the work is blocked or at risk, returning a confidence score and the
supporting message(s) as evidence. The system SHALL surface a detection only when its confidence is at
or above a configurable threshold. Detection SHALL run as part of the capture/summarization extraction
pass over a bounded, debounced window rather than re-scanning whole channels.

#### Scenario: Conversation signals a blocker above threshold

- **WHEN** a captured conversation for a work item with a linked channel contains a blocker or risk
  signal (e.g. "we're stuck on…", "blocked by…", a failing dependency) and Claude classifies it as a
  blocker at or above the confidence threshold
- **THEN** the system raises a blocker detection for that work item with the supporting message(s) as
  evidence

#### Scenario: Signal below the confidence threshold

- **WHEN** Claude classifies a window as a possible blocker below the configured confidence threshold
- **THEN** the system does not raise a detection and takes no notification action

#### Scenario: Claude unavailable

- **WHEN** `Anthropic:ApiKey` is not configured and the fake extractor is in use
- **THEN** the system does not raise spurious detections and continues capturing without failing

### Requirement: Notify the assignee and reporter in the channel

When a blocker detection is surfaced, the system SHALL post a message in the work item's linked Slack
channel (resolved via `IMappingStore`) that @-mentions the work item's assignee and reporter, resolving
their Jira accountIds (from the mirrored `WorkItem`) to Slack ids via `IJiraSlackIdentityResolver`
(config `UserMap`) and posting via `ISlackClient`. The system SHALL skip any participant whose Slack
identity cannot be resolved and SHALL not fail capture if the post fails.

#### Scenario: Detection notifies resolvable participants

- **WHEN** a blocker detection is surfaced for a work item whose assignee and reporter resolve to Slack
  ids
- **THEN** the system posts a message in the work item's channel that @-mentions the assignee and
  reporter and references the detected blocker

#### Scenario: Unresolvable identity skipped

- **WHEN** a participant's Jira accountId does not resolve to a Slack id via the configured `UserMap`
- **THEN** the system omits that @-mention and still posts the notification to the channel

#### Scenario: No linked channel

- **WHEN** a blocker is detected for a work item that has no linked channel in `IMappingStore`
- **THEN** the system takes no notification action

### Requirement: Optional Jira annotation

When Jira annotation is enabled in configuration, the system SHALL annotate the work item's Jira issue
for a surfaced detection by adding a managed `blocked` label and/or a managed comment via
`IWriteBackService`. Jira annotation SHALL be disabled by default so the Slack notification can operate
without writing to Jira.

#### Scenario: Annotation enabled

- **WHEN** a blocker detection is surfaced and Jira annotation is enabled
- **THEN** the system adds the managed `blocked` label and/or a managed comment to the Jira issue via
  `IWriteBackService`

#### Scenario: Annotation disabled

- **WHEN** a blocker detection is surfaced and Jira annotation is disabled
- **THEN** the system posts the Slack notification and makes no write to the Jira issue

### Requirement: De-duplicate detections per work item

The system SHALL persist each raised detection keyed by work item with a signature and SHALL suppress a
new detection whose signature matches an already-raised or dismissed detection within a configurable
window, so the same ongoing blocker is not re-flagged on subsequent messages.

#### Scenario: Repeated mentions of the same blocker

- **WHEN** the same blocker is referenced again in later captured messages for a work item that already
  has a matching raised detection
- **THEN** the system does not post a new notification for the duplicate detection

#### Scenario: A distinct second blocker

- **WHEN** a captured conversation signals a blocker whose signature differs from any already-raised or
  dismissed detection for that work item
- **THEN** the system raises and notifies the new detection

### Requirement: Human dismissal of a detection

The system SHALL allow a human to dismiss a detection via an action on the channel notification, handled
under the `/slack` endpoints. A dismissed detection SHALL be recorded so that de-duplication suppresses
matching detections and the same blocker is not re-flagged.

#### Scenario: User dismisses a false positive

- **WHEN** a user invokes the dismiss action on a blocker notification
- **THEN** the system records the detection as dismissed and does not re-raise a matching detection

#### Scenario: Dismissed blocker recurs in conversation

- **WHEN** a previously dismissed blocker's signature reappears in later captured messages within the
  dedup window
- **THEN** the system does not post a new notification for it
