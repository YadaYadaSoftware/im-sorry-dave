## Context

Jira is the source of truth; the platform mirrors each work item, provisions a Slack channel per work
item, captures conversation as a `CapturedMessage` transcript, and records confirmed decisions as
`WriteBackRecord`. A per-channel `PostCursor` already marks the last successful `/post` — but it is
**shared**, so it answers "what has the platform posted," not "what has *this user* seen." `IMappingStore`
resolves channel↔work-item, and `IDecisionExtractor` (gated on `Anthropic:ApiKey`, with a fake fallback)
already talks to Claude. This change adds a per-user read cursor and a `/catchup` command that turns
the delta since that cursor into a personal digest.

## Goals / Non-Goals

**Goals:**
- `/catchup` in a work-item channel (or DM) returns an **ephemeral** digest to the invoking user only.
- The digest covers everything since **that user's** personal read cursor: `WorkItem` status/assignee
  changes, new Jira comments, confirmed decisions (`WriteBackRecord`), and notable conversation
  (`CapturedMessage`).
- Claude summarizes the delta; absent `Anthropic:ApiKey`, fall back to a grounded plain listing.
- After a successful digest, advance the user's cursor; handle "nothing new" without advancing visibly.

**Non-Goals:**
- Touching or reinterpreting the shared `PostCursor` (it stays the `/post` marker).
- Changing what gets written back to Jira or how decisions are extracted.
- A scheduled/automatic digest (this is on-demand `/catchup` only).
- Cross-work-item or org-wide digests (one work item per invocation).

## Decisions

### Per-user read cursor distinct from PostCursor
**Decision:** Add a per-`(user, channel/work-item)` **read cursor** keyed by Slack user id + work-item
(resolved via `IMappingStore`). It records how far that user has been caught up — a timestamp/sequence
marker comparable across the digest sources. It is **separate** from `PostCursor`: `PostCursor` is one
shared marker per channel for `/post`; the read cursor is one per user per work item. *Why separate:*
the two answer different questions and advance on different events; overloading `PostCursor` would
corrupt `/post` behavior. *First-time users* (no cursor yet) default to a sensible baseline (e.g.
channel/work-item creation or a bounded look-back) so the first `/catchup` is useful, not unbounded.

### Sources that feed the digest
**Decision:** Assemble the delta since the user's cursor from existing seams only:
- **`WorkItem`** — status and assignee changes (the mirrored fields).
- **Jira comments** — new comments since the cursor.
- **`WriteBackRecord`** — confirmed decisions/answers since the cursor.
- **`CapturedMessage`** — notable conversation since the cursor.
No new capture pipeline; `/catchup` is a read-side projection over what is already mirrored.

### Claude summarizes the delta, graceful without a key
**Decision:** Feed the assembled delta to the Claude summarizer (the `IDecisionExtractor` seam / a
sibling summarize call), gated on `Anthropic:ApiKey`. With a key, produce a concise narrative grouped
by category. **Without** a key (fake fallback), emit a deterministic grounded listing of the same delta
(status/assignee changes, new comments, decisions, notable messages) — never fabricated. The command
must work in both modes.

### Ephemeral response to the invoking user only
**Decision:** Respond on the signature-verified slash endpoint (`/slack/commands`) with an **ephemeral**
message (visible only to the invoker), so each user's catch-up is private and does not spam the channel.
Works the same in a channel or a DM. `IMappingStore` resolves the channel to its work item; a DM or an
unmapped channel yields a clear ephemeral "no work item linked here" reply.

### Advance the cursor only on a delivered digest; handle "nothing new"
**Decision:** If there is **nothing new** since the user's cursor, reply ephemerally "You're all caught
up — nothing new since you last caught up" and do **not** move the cursor. If there is a delta, deliver
the digest and **then** advance the cursor to the high-water mark of what was included, so the next
`/catchup` starts from there. Advancing only after a successful delivery avoids skipping a delta if the
summarize/post step fails.

## Risks / Trade-offs

- [Cursor drift vs. new arrivals] → first-time/no-cursor users get a bounded baseline, not the entire
  history, so an initial `/catchup` stays concise.
- [No Anthropic key] → fall back to a deterministic grounded listing; the command still answers "what
  changed," just without prose.
- [Summarize/post failure after computing the delta] → advance the cursor only after a successful
  delivery, so a failure re-shows the same delta rather than losing it.
- [Noisy transcript] → cap the `CapturedMessage` slice and prefer notable/decision-bearing messages so
  the digest stays concise.
- [Privacy] → ephemeral reply keeps each user's catch-up private; sources are already visible to channel
  members, so no new exposure.

## Migration Plan

1. Add the per-`(user, work-item)` read-cursor store (persistence parallel to `PostCursor`), with a
   baseline default for first-time users.
2. Add the `/catchup` handler on `/slack/commands`: verify signature, resolve work item via
   `IMappingStore`, load the user's cursor.
3. Assemble the delta since the cursor from `WorkItem`, Jira comments, `WriteBackRecord`, and
   `CapturedMessage`.
4. Summarize via Claude (key present) or the deterministic fallback (key absent); reply ephemerally.
5. On a delivered digest, advance the user's cursor; on "nothing new," leave it unchanged.
6. Tests: delta digest, no-key fallback, nothing-new, DM/unmapped channel, cursor advance/idempotency.
7. Deploy (SSM secrets, ECS); run `/catchup` after activity and confirm a private digest + advanced
   cursor.

## Open Questions

- Baseline for a first-time user with no cursor — work-item/channel creation vs. a fixed look-back
  window? Start with creation time; revisit if first digests are too long.
- How "notable" is decided for `CapturedMessage` — decision-bearing/mention-bearing first, then recency;
  tune the cap after real usage.
