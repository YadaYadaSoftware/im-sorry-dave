## Context

This change closes the loop: Slack conversation → Claude extraction → confirmed write-back to Jira. It depends on `slack-channel-provisioning` (channel↔work-item link, channel surface) and `jira-sync-core` (`jira-decision-writeback`). It is where the Anthropic Claude API enters the platform.

## Goals / Non-Goals

**Goals:**
- Faithfully capture work-item channel conversations as per-work-item transcripts.
- Use Claude to extract grounded, confidence-scored decisions/answers/summaries.
- Keep a human in the loop: nothing reaches Jira without confirmation.
- Offer both automatic detection and an on-demand summarize command.

**Non-Goals:**
- The Jira write mechanics, idempotency, and attribution storage (owned by `jira-decision-writeback`).
- Channel creation/lifecycle (owned by `slack-channel-provisioning`).
- A general-purpose Slack bot beyond summarization/confirmation.

## Decisions

- **Human-in-the-loop is mandatory.** Claude proposes; people confirm. Candidates are presented in Slack (interactive message) and only confirmed ones are written back. *Alternative:* auto-write high-confidence candidates — rejected for v1 (trust/accuracy risk against a source of truth).
- **Claude via the Anthropic API with structured output.** Use a current Claude model (default `claude-opus-4-8`; a cheaper Claude model is acceptable for routine extraction) with tool-use / structured JSON so candidates come back as typed records with evidence references and confidence. *Alternative:* free-text parsing — rejected (brittle, ungrounded).
- **Grounding + confidence required.** Each candidate must cite supporting messages; uncertain conclusions are flagged low-confidence. Reduces hallucinated decisions reaching Jira.
- **Conversation unit = thread or bounded channel window.** Extraction operates on a coherent unit to keep prompts focused and costs bounded; the summarize command picks the unit explicitly. *Alternative:* whole-channel-every-time — rejected (cost, noise).
- **Capture via Events API with signature verification + dedupe.** Store transcripts with thread fidelity; verify Slack signatures; dedupe redelivered events. Reconstructable threads make extraction reliable.
- **Redaction/consent policy is a gate.** A configurable policy runs before any content is forwarded to Jira; sensitive content is withheld or redacted. Protects against leaking secrets/PII into the system of record.
- **Triggering.** Automatic extraction runs on natural conversation boundaries (e.g., thread resolution, idle, or explicit cue) and is always available on demand via the slash command. Auto-runs are throttled to control API cost.

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

- What signals trigger automatic extraction (idle timer, reaction/emoji cue, thread "resolved", explicit only)?
- Which Claude model and effort/cost tier for routine vs. on-demand extraction?
- Redaction policy specifics (patterns, opt-in vs opt-out per channel).
- Where answers attach in Jira — comments vs. a structured Q&A field (coordinated with `jira-decision-writeback` open question).
