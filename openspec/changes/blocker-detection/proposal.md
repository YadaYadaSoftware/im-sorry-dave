## Why

Jira is the source of truth and each work item gets a Slack channel where its conversation is
captured (`CapturedMessage`). Today that conversation is only mined when someone explicitly asks for a
summary — so when a thread quietly signals trouble ("we're stuck on…", "blocked by the payments
team", "the build is still red"), nobody is notified until a human happens to read it or a stand-up
surfaces it. The platform already understands the conversation via Claude; it should proactively flag
a likely **blocker or risk** and pull the right people in, rather than waiting to be asked.

## What Changes

- **Claude-flagged blockers.** When a work item's captured conversation indicates a blocker or risk,
  Claude (`IDecisionExtractor`, gated on `Anthropic:ApiKey` with the fake fallback) classifies it as a
  blocker detection with a confidence score and the supporting message(s) as evidence.
- **Proactive notification.** A detection above the confidence threshold posts a message in the work
  item's Slack channel that @-mentions the **assignee and reporter** — resolving their Jira accountIds
  to Slack ids via `IJiraSlackIdentityResolver` (config `UserMap`) and posting through `ISlackClient`,
  skipping any whose identity cannot be resolved.
- **Optional Jira annotation.** A detection optionally labels the Jira issue (a managed `blocked`
  label) and/or adds a managed comment via `IWriteBackService`.
- **Dismiss false positives.** A human can dismiss a detection (the channel message offers a dismiss
  action); a dismissed detection is not re-raised.
- **De-duplication.** Detections are de-duplicated per work item so the same ongoing blocker is not
  re-flagged on every subsequent message.

## Capabilities

### New Capabilities
- `blocker-detection`: detect, from a work item's captured Slack conversation, that the work is
  blocked or at risk; notify the assignee/reporter in the channel and optionally annotate the Jira
  issue, with confidence gating, human dismissal, and per-work-item de-duplication.

## Impact

- `src/SorryDave.JiraSync.Core/Slack` — add a blocker-detection path that runs Claude over a bounded
  conversation window and posts the notification via `ISlackClient`.
- `IDecisionExtractor` — extend (or add a sibling) to classify a window as a blocker with confidence +
  evidence references; reuses the existing `Anthropic:ApiKey` gate and fake fallback.
- `IJiraSlackIdentityResolver` — resolve assignee/reporter accountIds (from the mirrored `WorkItem`)
  to Slack ids for the @-mention.
- `IWriteBackService` — optional `blocked` label / managed comment on the Jira issue.
- `IMappingStore` (or a sibling detection store) — persist raised/dismissed detections keyed by work
  item for de-duplication and dismissal.
- Slack endpoints under `/slack` — handle the dismiss interaction.
- Config — confidence threshold, whether Jira annotation is enabled, dedup window.
