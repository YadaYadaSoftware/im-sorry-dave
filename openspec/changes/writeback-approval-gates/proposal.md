## Why

Today every `SummaryCandidate` that Claude extracts is posted as an interactive Confirm/Reject card and
**must** be clicked by a human before `IWriteBackService.SubmitAsync` writes it back to Jira. That is the
right default for low-confidence or sensitive candidates, but it has two gaps: (a) obviously-correct,
high-confidence candidates still wait on a manual click, adding latency and toil; and (b) anyone in the
channel can confirm a candidate, even sensitive ones (e.g. a Decision on a regulated project) that should
only be approved by a named, accountable person. We need confidence and approver gates on write-back —
configurable, off by default so today's behavior is unchanged.

## What Changes

- **Auto-confirm gate.** When enabled, a candidate whose `Confidence` meets a configurable threshold
  (global, optionally overridden per `Kind`) is written back automatically — `ConfirmAsync` /
  `SubmitAsync` runs without a human click. Below-threshold candidates keep today's manual-confirm flow.
- **Required-approver gate.** When a rule matches a candidate (by `Kind`, Jira project, or both), only a
  configured **named approver** (a specific Slack user or group) may confirm it. A Confirm action from any
  other user is rejected (and the card is left pending) until the approver acts.
- **Card presentation reflects the gate.** Auto-confirmed candidates render as already-written (no
  Confirm/Reject buttons); approver-gated candidates state who must approve.
- **Config + safe defaults.** New `SlackOptions` (SSM-backed) section drives thresholds and approver
  rules. With no config, both gates are off and behavior is identical to today (every candidate requires a
  manual human Confirm).

## Capabilities

### New Capabilities
- `writeback-approval-gates`: Apply configurable confidence-threshold (auto-confirm) and named-approver
  gates in the Slack summarization confirm path before a `SummaryCandidate` is submitted to Jira
  write-back, defaulting to today's manual-confirm-everything behavior when unconfigured.

## Impact

- `IConversationSummarizer` (`ConfirmAsync` / `RejectAsync`) — evaluate gates before delegating to
  `IWriteBackService.SubmitAsync`; allow an internal auto-confirm path that bypasses the human click.
- `SummaryCandidate` (`Confidence`, `Kind`, `RecordIdentity`) — read by the gate evaluator; no shape
  change required beyond what already exists.
- Block Kit confirm/reject cards — render auto-confirmed candidates as already-written and approver-gated
  candidates with the required-approver hint.
- `/slack/interactivity` handler — on a Confirm action, check the acting user against the matched
  approver rule before calling `ConfirmAsync`.
- `SlackOptions` / configuration (SSM) — add thresholds-per-Kind and approver-rules-per-project/Kind.
- `IJiraSlackIdentityResolver` — resolve the acting Slack user / group membership to enforce approver rules.
- No new external dependency; AWS ECS deployment and SSM secrets convention unchanged.
