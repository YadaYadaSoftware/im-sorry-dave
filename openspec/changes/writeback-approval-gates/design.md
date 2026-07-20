## Context

The summarization flow is: `/post` → Claude extracts `SummaryCandidate`s (each carries `Kind`
∈ {Decision, Answer, Summary}, `Content`, `Confidence` ∈ [0,1], `RecordIdentity`) → each is posted as an
interactive Block Kit card with Confirm/Reject buttons → the `/slack/interactivity` endpoint routes the
action to `IConversationSummarizer.ConfirmAsync` / `RejectAsync` → `ConfirmAsync` calls
`IWriteBackService.SubmitAsync`, an idempotent outbox that writes a Jira comment. Every candidate
currently requires a manual human Confirm click, and any channel member can click it. `Confidence` is
already produced but unused for gating, and `IJiraSlackIdentityResolver` can already map Slack users.
This change inserts a gate evaluation step in the confirm path without changing the surrounding seams.

## Goals / Non-Goals

**Goals:**
- Auto-confirm high-confidence candidates (threshold global, optionally per `Kind`) so they write back
  without a human click; below-threshold candidates keep the manual-confirm flow.
- Enforce that approver-gated candidates (matched by `Kind`, project, or both) are confirmed only by a
  configured named Slack user/group; reject confirms from anyone else and leave the card pending.
- Reflect the gate outcome in the card (auto-confirmed → already-written; approver-gated → states the
  approver).
- Safe default: with no config, both gates are off and behavior is identical to today.

**Non-Goals:**
- Changing how candidates are extracted, scored, or how `SubmitAsync` writes to Jira (idempotency,
  attribution unchanged).
- A multi-approver / quorum or approval-chain workflow (single named approver or group per rule).
- A persistent approval audit store beyond what the outbox / Slack message state already records.
- Re-scoring or learning thresholds from history.

## Decisions

### Gate sits in the confirm path, immediately before `SubmitAsync`
**Decision:** Add a `IWriteBackGate` (or equivalent evaluator) consulted inside
`IConversationSummarizer.ConfirmAsync`, *after* the candidate and acting user are known and *before*
`IWriteBackService.SubmitAsync`. This is the single choke point every write-back passes through, so both
gates (auto-confirm and required-approver) are enforced in one place regardless of whether the trigger
was a human click or the auto path. *Why not at extraction/card render:* the acting user is only known at
interactivity time, and `SubmitAsync` idempotency must stay the final authority — gating earlier would
duplicate the decision.

### Auto-confirm runs the same path as a human confirm
**Decision:** When a candidate's `Confidence` ≥ the resolved threshold, the system invokes the existing
`ConfirmAsync` write-back path itself (a synthetic/system confirmation) right after the cards would be
posted, rather than waiting for a click. The card is then rendered in the already-written state. *Why
reuse `ConfirmAsync`:* keeps a single submit path (idempotent outbox, attribution) and avoids a parallel
write route. Auto-confirmations are attributed to the system actor, distinct from human confirmers.

### Threshold resolution: per-Kind overrides a global default
**Decision:** Config holds a global `AutoConfirmThreshold` and an optional per-`Kind` map. Resolution for
a candidate: per-`Kind` value if present, else global, else "no auto-confirm" (disabled). Thresholds are
in [0,1]; a missing/unset threshold means the Kind is never auto-confirmed (manual only). *Why per-Kind:*
teams trust Summaries differently from Decisions; one knob is too blunt.

### Required-approver: rule match → enforce acting user against the named approver
**Decision:** Config holds an ordered list of approver rules, each with an optional `Kind`, optional Jira
`Project` (derived from `RecordIdentity`), and a required `Approver` (a Slack user id or group id). A
candidate matches the first rule whose `Kind`/`Project` conditions it satisfies (a rule with neither
condition matches all). On a Confirm action at `/slack/interactivity`, if the candidate matches an
approver rule, resolve the acting Slack user via `IJiraSlackIdentityResolver` and verify they are the
named user or a member of the named group; if not, reject the action (ephemeral "only <approver> can
approve this") and leave the card pending. *Why first-match ordering:* lets a specific
project+Kind rule precede a broad catch-all without ambiguity.

### Auto-confirm yields to required-approver
**Decision:** If a candidate matches a required-approver rule, it is **not** auto-confirmed even if its
confidence clears the threshold — the named approver must act. Approver gates are a stronger control than
confidence and must not be bypassed by score. *Why:* high confidence is about correctness; an approver
gate is about accountability/authority, which confidence cannot satisfy.

### Card presentation per gate outcome
**Decision:** Auto-confirmed candidates render without Confirm/Reject buttons, showing an
"automatically recorded (confidence X)" state. Approver-gated candidates keep the buttons but add context
text naming the required approver, so non-approvers see why their click is rejected. *Why:* the card is
the user-visible contract; it must not invite a click that will be rejected.

### Safe defaults = today's behavior
**Decision:** Empty/absent config ⇒ no auto-confirm threshold (every Kind manual) and no approver rules
(any channel member may confirm) — byte-for-byte today's flow. Gates are opt-in via `SlackOptions`.

## Risks / Trade-offs

- [Auto-confirming a wrong high-confidence candidate] → conservative default (disabled), per-Kind
  thresholds, and `RejectAsync` still available to undo before/after; document recommended starting
  thresholds.
- [Misconfigured approver locks write-back] → if an approver id can't be resolved, fail closed (stay
  pending) and surface a clear ephemeral message; log the unresolved rule.
- [Group-membership check cost/latency at interactivity time] → resolve via
  `IJiraSlackIdentityResolver`, cache membership briefly; keep the check best-effort-fast and fail closed.
- [Threshold drift / over-trust] → thresholds live in SSM config and are reviewable; auto-confirmations
  are attributed to the system actor so they're auditable.
- [Two gates interacting confusingly] → explicit precedence (approver gate wins over auto-confirm),
  documented and covered by scenarios.

## Migration Plan

1. Add the `SlackOptions` config section (thresholds-per-Kind, ordered approver rules) with empty
   defaults; bind from SSM.
2. Add the gate evaluator and call it inside `ConfirmAsync` before `SubmitAsync`; add the system/auto
   confirm path after card posting.
3. Enforce approver rules in the `/slack/interactivity` Confirm handler using `IJiraSlackIdentityResolver`.
4. Update Block Kit cards for the already-written and approver-required states.
5. Tests: thresholds (global + per-Kind), approver match/mismatch, precedence, and the off-by-default
   equivalence to today.
6. Roll out with gates off; enable per environment via SSM config; verify in a non-prod project first.

## Open Questions

- Should a rejected non-approver Confirm be silently queued for the approver, or just rejected with the
  card left pending? (Starting position: reject + leave pending; the approver clicks later.)
- Should auto-confirmed records be visually distinguished from human-confirmed ones in Jira (system
  attribution), or only in Slack? (Starting position: system attribution in both.)
- For group approvers, is any group member sufficient, or do we later need a specific member? (Starting
  position: any member of the named group.)


> **Build any slash command as a plugin.** Since `slack-command-plugins` landed, commands are not wired
> into `SlackEventEndpoints`: each implements `ISlackCommandPlugin`, owns its interactivity actions under
> namespaced action ids (`plugin:action`), and is served only when named in the `Slack:EnabledCommands`
> allow-list. The host owns Slack's ack-then-background handling, so handlers need not manage it.
> Commands that write to Jira cannot currently be enabled.
