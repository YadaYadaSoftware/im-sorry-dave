## Context

Summarization already has the full loop: `/slack/events` captures `message.channels` into
`CapturedMessage` (signature-verified), `/post` calls `IConversationSummarizer.PostAsync(channelId)`
which extracts decisions over the window since the channel's `PostCursor.LastPostedTs` via the Claude
`IDecisionExtractor`, posts interactive Block Kit candidate cards, and `/slack/interactivity` runs
`ConfirmAsync`/`RejectAsync` — confirm writes back through the idempotent outbox and advances the
cursor. v1 triggering is explicit only. The platform already runs periodic sweeps as hosted
`*BackgroundService` classes (`SlackReconciliationBackgroundService`, `WriteBackBackgroundService`),
and Claude cost is an explicit concern. This change adds two optional triggers that reuse `PostAsync`
end-to-end so the confirmation gate and write-back path are untouched.

## Goals / Non-Goals

**Goals:**
- An idle work-item channel with new conversation gets auto-summarized into candidate cards.
- Reacting with a configured emoji summarizes the window around that message into candidate cards.
- Both are opt-in (off by default), throttled per channel, and de-duped against the `PostCursor`.
- Both still require human confirmation before any Jira write-back.

**Non-Goals:**
- Auto-confirming or auto-writing back (confirmation stays human; no unsolicited writes).
- Changing the extraction prompt, candidate-card format, or the outbox write-back.
- Summarizing channels not linked to a work item (unlinked channels stay ignored).
- Per-user reaction permissions beyond the channel membership Slack already enforces.

## Decisions

### Idle sweep as a hosted background service
**Decision:** Add `AutoSummarizeBackgroundService : BackgroundService`, registered only when
`SlackOptions.IdleSummarizeEnabled`, following `SlackReconciliationBackgroundService`. Every
`AutoSummarizeSweepInterval` it selects linked channels whose newest non-deleted `CapturedMessage` is
older than `IdleSummarizeAfter` AND has a `Ts` greater than `PostCursor.LastPostedTs` (i.e. unposted
conversation exists), and for each eligible channel calls the **same** `PostAsync(channelId)` the
`/post` command uses, then posts the returned candidate cards exactly as the slash handler does.
*Why reuse PostAsync:* the window selection, extractor call, candidate persistence, and cursor
semantics already live there — the trigger only decides *when*, never *how*.

### Reaction cue on /slack/events
**Decision:** Extend the existing `event_callback` branch in `SlackEventEndpoints` to handle
`event.type == "reaction_added"`. When `reaction` equals the configured `ReactionCueEmoji` (default
`memo`), the `item` is a message in a linked channel, and the trigger is enabled, run a summarize over
that channel (`PostAsync(channelId)`). Ack within 3s and do the Claude work fire-and-forget in its own
DI scope, mirroring the `/commands` handler. Requires the Slack app to subscribe `reaction_added` and
hold the `reactions:read` scope. *Why the same endpoint:* it is already signature-verified and is the
single inbound Slack seam; no new endpoint or verifier.

### Window de-dup against the PostCursor
**Decision:** Both triggers go through `PostAsync`, which already summarizes only messages after
`LastPostedTs` and advances the cursor only on a successful confirmed write-back. So a manual `/post`
and an auto run target the same window definition and never write the same decision twice; the
idempotent outbox is the final backstop. The reaction cue summarizes the channel's since-cursor window
(not an isolated thread slice) to keep one cursor authority and avoid overlapping windows. *Why not a
second cursor:* two cursors would race and could double-summarize; one cursor is the de-dup authority.

### Per-channel cooldown to bound Claude cost
**Decision:** A trigger gate records the last auto-trigger time per channel and refuses to fire again
within `AutoSummarizeCooldown` (default e.g. 15 min). The idle sweep also won't re-fire a channel with
no new messages since its last run (the cursor/idle check already covers this). The explicit `/post`
command bypasses the cooldown (human intent is always honored). *Why:* a chatty-then-idle channel, or
rapid repeated reactions, must not trigger back-to-back extractions and run up spend.

### Both default off; scoping mirrors provisioning
**Decision:** `IdleSummarizeEnabled` and `ReactionCueEnabled` default `false`. Scoping reuses the
existing convention: an optional channel allow-list and reuse of `EligibleIssueTypes`-style gating so
auto triggers can be limited to the same issue types that get channels. *Why off by default:* existing
deployments must see no behavior or cost change until an operator opts in.

## Risks / Trade-offs

- [Claude cost from auto runs] → opt-in + per-channel cooldown + cursor de-dup; explicit `/post` is the
  only un-throttled path and is human-initiated.
- [Reaction by anyone in channel triggers spend] → cooldown bounds frequency; scope to issue types /
  channel allow-list; only the configured emoji fires.
- [Idle sweep summarizing mid-conversation] → require both idleness (no message for N minutes) and
  unposted conversation; a still-active channel is skipped until it quiets.
- [Missing `reactions:read` scope] → reaction events simply won't arrive; document the scope +
  subscription as a deploy prerequisite, and no-op gracefully if Slack is unconfigured.
- [Double-trigger from auto + manual] → single `PostCursor` is the window authority; outbox idempotency
  is the final guard.

## Migration Plan

1. Add the new `SlackOptions` fields (all default off / conservative) — no behavior change yet.
2. Add the trigger gate (cooldown + cursor de-dup) and `AutoSummarizeBackgroundService`; register it
   only when `IdleSummarizeEnabled`.
3. Add `reaction_added` handling to `/slack/events` behind `ReactionCueEnabled`.
4. Tests: idle eligibility (idle + unposted vs. active/empty), cooldown suppression, reaction cue on
   linked vs. unlinked channel and wrong emoji, no-double-summarize across auto + `/post`.
5. Update the Slack app: subscribe `reaction_added`, add `reactions:read` scope; document both.
6. Deploy with triggers off; enable per-channel in a pilot, confirm candidate cards appear and still
   require confirmation, then widen scope.

## Open Questions

- Reaction cue window: since-cursor channel window (chosen, single-cursor) vs. just the reacted
  thread — start with since-cursor; revisit if operators want thread-scoped cues.
- Cooldown default (15 min) and idle default (e.g. 30 min) — tune from pilot Claude spend.
