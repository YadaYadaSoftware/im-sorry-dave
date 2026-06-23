## Context

The platform captures each work item's Slack conversation as `CapturedMessage` rows and already runs
Claude over conversation windows for summarization/extraction (`IDecisionExtractor`, gated on
`Anthropic:ApiKey` with a fake fallback so the system runs without a key). Jira is the source of
truth; the mirrored `WorkItem` carries the assignee/reporter accountIds and display names, and
`IJiraSlackIdentityResolver` maps a Jira accountId to a Slack id via the config `UserMap`. Channels
are linked to work items through `IMappingStore`. This change reuses those seams to turn passive
capture into a proactive blocker/risk signal — the first capability that *acts* on the conversation
without being explicitly asked.

## Goals / Non-Goals

**Goals:**
- Detect, from a work item's captured conversation, that it is blocked or at risk.
- Notify the people who can act — the assignee and reporter — in the work item's channel.
- Keep Claude cost bounded and avoid re-flagging the same blocker.
- Let a human dismiss a false positive so it stays dismissed.

**Non-Goals:**
- Resolving or tracking the blocker to closure (no state machine beyond raised/dismissed).
- Auto-transitioning the Jira issue or auto-reassigning (annotation is a label/comment only).
- Replacing human-driven summarization (`slack-conversation-summarization`); this is additive.
- Building a general risk/sentiment dashboard.

## Decisions

### Detect as part of the capture/summarization extraction pass, not a separate sweep
**Decision:** Run blocker classification on the existing Claude extraction path over a **bounded
window** of recent `CapturedMessage`s for the work item, rather than a standalone periodic sweep of
every channel. Detection is triggered when new messages are captured (debounced) and naturally
piggybacks on the window the platform already assembles. *Why:* reuses the assembled transcript and
the `IDecisionExtractor` gate/fallback, and bounds Claude cost to the delta rather than re-scanning
whole channels on a timer. *Alternative — periodic sweep of all channels:* rejected for v1 (scans
idle channels, multiplies Claude cost); a sweep can be added later for catch-up without changing the
contract.

### Claude returns a confidence-scored, evidence-grounded blocker classification
**Decision:** Extend `IDecisionExtractor` (or add a sibling method) to classify a window as a
blocker/risk, returning a typed result with a **confidence** score and the **supporting
message id(s)** as evidence. Only detections at or above a configurable **confidence threshold** are
surfaced. *Why:* mirrors the platform's existing grounded-with-confidence extraction and avoids
notifying on weak signals. When `Anthropic:ApiKey` is absent the fake extractor returns no detections
(or a deterministic stub) so nothing spurious is posted.

### De-duplicate detections per work item with a signature
**Decision:** Persist each raised detection keyed by work item with a **signature** (e.g. a normalized
blocker summary / evidence fingerprint) in `IMappingStore` (or a sibling detection store). Before
surfacing, suppress a new detection whose signature matches an already-raised-or-dismissed one within
a configurable window. *Why:* a live blocker is mentioned across many messages; without dedup every
follow-up message would re-notify. *Alternative — one open detection per work item:* simpler but
coarse (a genuinely different second blocker would be swallowed); the signature approach distinguishes
distinct blockers while still collapsing repeats.

### Notify assignee and reporter in-channel via identity resolution
**Decision:** On a surfaced detection, post a message to the work item's linked channel (looked up via
`IMappingStore`) that @-mentions the assignee and reporter. Resolve their accountIds (from the
mirrored `WorkItem`) to Slack ids via `IJiraSlackIdentityResolver`; post via `ISlackClient`. Skip any
participant whose Slack identity does not resolve; if none resolve, still post the unaddressed
notification so the channel sees it. Best-effort: a failed post never crashes capture. *Why:* the
channel is the platform's surface and the @mention notifies the accountable people directly.

### Optional Jira annotation behind config
**Decision:** When Jira annotation is enabled, add a managed `blocked` label and/or a managed comment
to the issue via `IWriteBackService` (which owns idempotency/attribution). Disabled by default so the
Slack notification can ship without writing to the source of truth. *Why:* keeps the source of truth
quiet until teams opt in, and reuses the existing write-back seam rather than a bespoke Jira call.

### Human dismissal via a channel action
**Decision:** The notification carries a **dismiss** action handled under `/slack`; dismissing records
the detection signature as dismissed so dedup suppresses it going forward. *Why:* false positives are
expected from any classifier; a one-click dismiss keeps the signal trusted and quiet.

## Risks / Trade-offs

- [Claude cost on chatty channels] → operate on a bounded, debounced window; prefer the cheaper Claude
  model for routine classification; piggyback on the existing extraction pass.
- [False-positive notifications eroding trust] → confidence threshold + one-click dismiss + dedup so a
  dismissed/known blocker never re-fires.
- [Notification spam on a long-running blocker] → per-work-item signature dedup within a window.
- [Unresolvable assignee/reporter identity] → resolve via `IJiraSlackIdentityResolver`, skip the
  unresolved mention, still post the notification.
- [Annotating the source of truth incorrectly] → Jira annotation off by default and routed through the
  managed `IWriteBackService` path so labels/comments are idempotent and attributed.

## Migration Plan

1. Add the blocker classification to the Claude extraction path (`IDecisionExtractor` extension +
   fake fallback) returning confidence + evidence.
2. Add the detection store (raised/dismissed signatures keyed by work item) and dedup check.
3. Wire the in-channel notification: resolve assignee/reporter via `IJiraSlackIdentityResolver`, post
   via `ISlackClient`, include the dismiss action; handle dismissal under `/slack`.
4. Add optional Jira annotation via `IWriteBackService` behind config (off by default).
5. Pilot on one channel with a low-stakes work item; tune the confidence threshold; then enable Jira
   annotation if desired.
- *Rollback:* disable detection via config; capture and summarization are unaffected.

## Open Questions

- None outstanding for this change.

**Resolved:** detection runs on the **capture/summarization extraction pass** over a bounded,
debounced window (periodic sweep deferred); surfacing is gated by a **configurable confidence
threshold**; dedup is by **per-work-item signature** within a window; notification @-mentions the
**assignee and reporter** via `IJiraSlackIdentityResolver` + `ISlackClient`; **Jira annotation is
optional and off by default** via `IWriteBackService`.
