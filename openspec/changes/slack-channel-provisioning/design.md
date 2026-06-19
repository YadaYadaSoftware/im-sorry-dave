## Context

Conversations need a home. We give a work item a dedicated Slack channel — created **lazily, on
first need** rather than for every item — and keep that channel linked to the work item via the
core mapping store. This change owns channel creation, lifecycle, membership, and context
reflection. It depends on `jira-sync-core` for the work-item model, the mapping store, and Jira
status-change events. The primary use case is Jira Product Discovery **ideas** (the `MDP`
project), which are relatively few and discussion-heavy.

## Goals / Non-Goals

**Goals:**
- A dedicated Slack channel per work item, provisioned **lazily** (on first need) and named deterministically.
- Lifecycle tied to work-item state: archive on close, unarchive on reopen.
- Durable, unique channel ↔ work-item link in the shared mapping store.
- Keep channel context (status, assignee, Jira link) current.

**Non-Goals:**
- Capturing or summarizing channel messages (that is `slack-conversation-summarization`).
- Writing anything back to Jira (that is `jira-decision-writeback`).
- Defining which items are "tracked" (owned by `jira-sync-core` scope config).

## Decisions

### Channel per work item (not a thread)

**Decision:** Each work item that gets discussion is given its **own Slack channel**, rather than
a **thread** inside a shared channel. Channels are provisioned lazily (see next decision) and
scoped to discussion-worthy issue types.

**Why this is the right call for this product** — the deciding factor is *attribution for the
summarize-back-to-Jira loop*. The whole platform exists to capture a conversation, have Claude
distill a decision/answer, and write it back to exactly **one** Jira item. The conversation
surface must make "this discussion belongs to MDP-5" unambiguous:

- **Channel per item** makes attribution trivial and robust: `channel_id → work item` (the 1:1
  mapping the core mapping store already models). Every message in the channel is, by
  construction, about that item. Capture, summarization, and write-back all key off the channel.
- **Thread per item** makes attribution `thread_ts → work item`, which *leaks* in practice:
  participants routinely post top-level messages in the parent channel instead of replying in the
  thread, and those messages are orphaned (which item are they about?). We'd be fighting Slack's
  UX to keep every relevant message inside the right thread.

Secondary advantages of channels that reinforce the choice:

- **Lifecycle maps cleanly.** A channel can be archived when the item closes and unarchived on
  reopen — mirroring the item's lifecycle. Threads cannot be archived or renamed; they just age
  out, leaving no lifecycle hook.
- **Per-item membership & notifications.** A channel can have the assignee/reporter/watchers as
  members, each able to mute/notify per item. Thread "following" is clunky and easy to miss.
- **Discoverability & context surface.** A channel has a name, topic, purpose, and pinned message
  to carry the work-item context. A thread has only its root message.

**Alternatives considered:**

- **Thread per item in a shared channel** — *rejected as the default* for the attribution leak,
  the lack of an archive/lifecycle hook, and threads being second-class in Slack (missed easily,
  no per-thread membership). Its one real strength — no channel sprawl — is addressed instead by
  lazy provisioning below.
- **Hybrid: channel per epic/feature, thread per child item** — *deferred, not rejected.* This is
  the right model if volume becomes very high (thousands of active items), trading some
  attribution cleanliness for far fewer channels. Our primary use case (JPD ideas) is low-volume
  and discussion-heavy, so per-idea channels are comfortable. Revisit if applied to a high-volume
  delivery board.

**Cost we accept and mitigate:** channel sprawl. Mitigated by lazy provisioning + issue-type
scoping + archive-on-close (below). If those prove insufficient at scale, the hybrid model is the
escape hatch.

### Lazy, scoped provisioning

**Decision:** Do **not** create a channel for every tracked item. Create one only when a
provisioning trigger fires (an explicit request to discuss — console/slash command or an @mention
of the item — or the item entering an active/in-progress state), and only for configured
discussion-worthy issue types (for `MDP`, the *Idea* type). *Why:* most items never need a
dedicated discussion space; lazy + scoped provisioning is what makes channel-per-item affordable
and keeps the sidebar/search usable. *Alternative:* provision for every tracked item — rejected
(sprawl, mostly-empty channels, channel-create rate limits).

### Supporting decisions

- **Archive, never delete.** Closed work items archive their channel to preserve history; reopening unarchives. *Alternative:* delete on close — rejected (loses conversation history that may still be referenced).
- **Deterministic naming from the work-item key.** Channel name derives from the Jira key (e.g., `mdp-5-express-checkout`) normalized to Slack rules, with a deterministic suffix on collision. Discoverable and idempotent to provision. *Alternative:* random names — rejected (undiscoverable, hard to dedupe).
- **Driven by Jira events from the core.** Lifecycle and context updates react to work-item created/status-changed/assignee-changed signals from `jira-work-item-sync`, not Slack polling.
- **Identity resolution is best-effort.** Map Jira users to Slack users by email where available; if unresolved, continue without failing and record the skip. *Alternative:* hard-require mapping — rejected (would block provisioning).
- **Context reflected via topic/purpose + pinned message.** Status/assignee in the topic; Jira link pinned. Low-noise; avoids spamming on every minor change.

## Risks / Trade-offs

- [Channel sprawl at scale] → Lazy + scoped provisioning, archive-on-close; hybrid (channel-per-epic + thread-per-item) as the escape hatch if volume demands it.
- [Slack channel-count / rate limits] → Lazy creation, batch and back off on `rate_limited`, reuse archived channels on reopen.
- [Channel-name collisions across projects] → Include project + key in the derived name; deterministic suffix as fallback.
- [Noisy status updates] → Debounce/reflect only meaningful transitions (status, assignee), not every field change.
- [Orphaned channels if mapping store and Slack drift] → Periodic reconciliation of links against Slack channel state.
- [User cannot be invited (not in workspace)] → Skip and record; do not fail provisioning.

## Migration Plan

1. Create and configure the Slack App (bot scopes, event/install).
2. Implement lazy provisioning + mapping; dry-run against a test work item (trigger via console command).
3. Wire the provisioning triggers (explicit request / active-status transition) for the in-scope issue types.
4. Enable lifecycle (archive/unarchive) behind a flag; verify on a closed test item.
- *Rollback:* disable provisioning; existing channels remain (archived links preserved).

## Open Questions

- Should channels be public or private by default?
- Exact provisioning trigger(s) to enable first — explicit request only, or also auto on active-status transition?
- Naming convention details (prefix, slug length) and whether to include the summary slug.
