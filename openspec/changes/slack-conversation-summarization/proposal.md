## Why

Decisions and answers are made in Slack conversations but rarely make it back into Jira, so Jira drifts from reality and context is lost. We need to capture the conversation in each work-item channel, use Claude to distill decisions and answered questions, and write confirmed results back to Jira so the source of truth stays accurate without manual copy-paste.

## What Changes

- Capture messages from work-item Slack channels via the Slack Events API and store them as a conversation transcript per work item.
- Use Claude to analyze the conversation and extract structured candidates: decisions, answers to open questions, and a running/closing summary.
- Require lightweight human confirmation (in Slack) before a candidate is written back to Jira, to avoid recording unconfirmed or speculative conclusions.
- Provide on-demand summarization via a Slack command (e.g., summarize this thread / channel now) in addition to automatic detection.
- Trigger write-back to Jira through the core `jira-decision-writeback` capability, with idempotency and attribution.
- Detect open questions raised in Slack and record them on the work item so answers can be tracked.

## Capabilities

### New Capabilities
- `slack-conversation-capture`: Ingest and store Slack messages/threads from work-item channels as per-work-item transcripts.
- `claude-decision-extraction`: Use Claude to extract decisions, answered questions, and summaries from a conversation as structured, confidence-scored candidates.
- `summary-writeback-trigger`: Gate candidates through human confirmation and on-demand commands, then submit confirmed records to Jira write-back.

### Modified Capabilities
<!-- None - new capabilities. Depends on slack-jira-linkage and jira-decision-writeback. -->

## Impact

- New dependency on the Anthropic Claude API (model `claude-opus-4-8` or a cost-appropriate Claude model) and prompt/templating for extraction.
- Requires Slack Events API subscriptions (`message.channels`, `app_mention`) and a slash command, plus interactive components for confirmation.
- Depends on `slack-jira-linkage` (channel→work-item resolution) and `jira-decision-writeback` (idempotent, attributed write-back).
- Handles potentially sensitive conversation content; redaction/consent policy applies before anything is sent to Jira.
