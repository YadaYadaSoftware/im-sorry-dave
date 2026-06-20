## Context

This change closes the loop: Slack conversation → Claude extraction → confirmed write-back to Jira. It depends on `slack-channel-provisioning` (channel↔work-item link, channel surface) and `jira-sync-core` (`jira-decision-writeback`). It is where the Anthropic Claude API enters the platform.

## Goals / Non-Goals

**Goals:**
- Faithfully capture work-item channel conversations as per-work-item transcripts.
- Use Claude to extract grounded, confidence-scored decisions/answers/summaries.
- Keep a human in the loop: nothing reaches Jira without confirmation.
- Offer explicit summarization — a slash command and an emoji-reaction cue (automatic detection deferred).

**Non-Goals:**
- The Jira write mechanics, idempotency, and attribution storage (owned by `jira-decision-writeback`).
- Channel creation/lifecycle (owned by `slack-channel-provisioning`).
- A general-purpose Slack bot beyond summarization/confirmation.

## Decisions

- **Human-in-the-loop is mandatory.** Claude proposes; people confirm. Candidates are presented in Slack (interactive message) and only confirmed ones are written back. *Alternative:* auto-write high-confidence candidates — rejected for v1 (trust/accuracy risk against a source of truth).
- **Claude via the Anthropic API with structured output.** Default model `claude-opus-4-8` for extraction accuracy (its output feeds a system of record), configurable down to `claude-haiku-4-5` for high-volume/routine runs. Use tool-use / structured JSON so candidates come back as typed records with evidence references and confidence. *Alternative:* free-text parsing — rejected (brittle, ungrounded).
- **Grounding + confidence required.** Each candidate must cite supporting messages; uncertain conclusions are flagged low-confidence. Reduces hallucinated decisions reaching Jira.
- **Conversation unit = thread or bounded channel window.** Extraction operates on a coherent unit to keep prompts focused and costs bounded; the summarize command picks the unit explicitly. *Alternative:* whole-channel-every-time — rejected (cost, noise).
- **Capture via Events API with signature verification + dedupe.** Store transcripts with thread fidelity; verify Slack signatures; dedupe redelivered events. Reconstructable threads make extraction reliable.
- **Redaction on by default.** A baseline redaction policy (well-known secret/token patterns) is enabled by default and applied to **both** the text sent to Claude and the content written back to Jira; the pattern set is configurable. Per-channel opt-out is *deferred*. Human confirmation remains the final backstop. *Why:* public channels feeding a system of record warrant safe-by-default redaction.
- **Explicit triggering first.** In v1, extraction runs only on an explicit cue: the summarize slash command, or reacting to a message/thread with a configured emoji (e.g., `:memo:`). Fully automatic extraction (idle timer / thread-resolved) is *deferred* — it adds cost and confirmation noise with little gain while a human still confirms everything. *Why:* matches the platform's explicit-first stance and bounds Claude cost. Auto-runs, if later enabled, will be throttled.
- **Decisions/answers written as Jira comments.** Confirmed candidates are written via the existing `jira-decision-writeback` capability as managed comments — answers linked to their originating question. A structured Q&A custom field is *deferred*. *Why:* reuses the built, verified write-back path and avoids per-project custom-field setup.
- **Secrets follow the platform convention** (`secrets-configuration-convention`). The Anthropic API key is an SSM parameter `/jira-sync/Anthropic/ApiKey` resolved as the config key `Anthropic:ApiKey` (user-secrets locally); Slack scopes reuse the tokens from `slack-channel-provisioning`. No bespoke secret delivery.

## Risks / Trade-offs

- [Claude cost on chatty channels] → Operate on bounded units, debounce auto-runs, prefer a cheaper Claude model for routine extraction, cache transcript state.
- [Hallucinated or premature decisions] → Grounding + confidence + mandatory human confirmation before write-back.
- [Sensitive content sent to Jira/Claude] → Redaction/consent gate; configurable allowed scope.
- [Confirmation fatigue / noise] → Only present medium/high-confidence candidates automatically; batch presentation.
- [Event loss or duplication] → Signature verify + idempotent transcript storage; reconciliation against Slack history for the summarize command.

## Migration Plan

1. Add Events API subscriptions, slash command, and interactivity endpoint to the Slack App.
2. Implement capture + transcript storage; verify on a test channel.
3. Integrate Claude extraction with structured output; validate grounding/confidence on sample transcripts.
4. Wire confirmation → `jira-decision-writeback` behind a flag; pilot on one work item before broad rollout.
- *Rollback:* disable auto-extraction and the command; capture can remain (passive) or be disabled independently.

## Open Questions

- None outstanding for this change.

**Resolved:** triggering is **explicit only** in v1 (slash command + emoji-reaction cue; automatic
detection deferred); default model **`claude-opus-4-8`**, configurable to **`claude-haiku-4-5`**
for cost; **baseline redaction on by default** applied to Claude input and Jira output (per-channel
opt-out deferred); decisions/answers are written as **Jira comments** via `jira-decision-writeback`
(structured Q&A field deferred).
