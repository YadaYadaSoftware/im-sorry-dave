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

### Lazy provisioning via explicit request (first trigger)

**Decision:** Do **not** create a channel for every tracked item. Create one only when an
**explicit request** to discuss the item fires — a console/slash command, a "Discuss in Slack"
action, or a bot @mention naming the item — and only for configured discussion-worthy issue
types (for `MDP`, the *Idea* type). *Why:* explicit-request is deterministic and sprawl-free —
channels exist only where someone actually intends to discuss, which also means the summarization
capability only ever captures meaningful conversations. *Alternatives:* provision for every
tracked item — rejected (sprawl, mostly-empty channels, rate limits). **Auto-provision on
active-status transition** (e.g., idea → "Under consideration") — *deferred*, a natural opt-in
second trigger once usage patterns are known. **Mention/keyword scanning across the workspace** —
rejected for now (broad message-read scopes + privacy/dedupe cost).

### Public channels by default

**Decision:** Provisioned channels are **public** by default. *Why:* discoverability and
workspace-wide search are the point of product-discovery ideas — broad input and durable org
memory — and public channels keep the bot's scopes simple and avoid invite-management becoming
load-bearing (our Jira→Slack identity mapping is only best-effort). *Alternative:* private by
default — rejected for the idea use case (kills discoverability, makes invites a hard dependency).
A **per-item override to private** (driven by a label such as `confidential` or a project/issue-
type rule) is *deferred* as a follow-on for sensitive ideas. Note: Slack does not allow freely
flipping a channel's visibility later, so the default is sticky — an at-creation override is the
intended mechanism rather than post-hoc conversion.

### Supporting decisions

- **Archive, never delete.** Closed work items archive their channel to preserve history; reopening unarchives. *Alternative:* delete on close — rejected (loses conversation history that may still be referenced).
- **Deterministic naming: `<jira-key>-<short-summary-slug>`.** The channel name is the Jira key, a hyphen, and a short slug of the work item's summary (e.g., MDP-7 "Build Slack Channel" → `mdp-7-build-slack-channel`). Normalized to Slack rules: lowercased (Slack requires lowercase), non-alphanumeric runs collapsed to single hyphens, trimmed, with the **key always preserved** and the **summary slug truncated** so the full name fits Slack's 80-char limit. A deterministic suffix is appended on collision. *Why:* the key guarantees discoverability/dedup; the slug makes it human-readable. *Alternative:* random names — rejected (undiscoverable, hard to dedupe).
- **Driven by Jira events from the core.** Lifecycle and context updates react to work-item created/status-changed/assignee-changed signals from `jira-work-item-sync`, not Slack polling.
- **Identity resolution is best-effort.** Map Jira users to Slack users by email where available; if unresolved, continue without failing and record the skip. *Alternative:* hard-require mapping — rejected (would block provisioning).
- **Context reflected via topic/purpose + pinned message.** Status/assignee in the topic; Jira link pinned. Low-noise; avoids spamming on every minor change.
- **Build against the real Slack Web API — no seeded fake.** Unlike the Jira client's in-memory fallback, the Slack client targets a real workspace; provisioning requires a configured bot token. Unit tests use an `ISlackClient` test double, and a credential-gated integration test covers the real create → seed → archive round-trip. *Why:* validate directly against Slack rather than a simulated workspace. *Trade-off:* the service can't be exercised end-to-end without Slack credentials (accepted).
- **Secrets follow the platform convention** (`secrets-configuration-convention`). The Slack bot token and signing secret are SSM parameters under the `/jira-sync/` prefix (`/jira-sync/Slack/BotToken`, `/jira-sync/Slack/SigningSecret`), resolved as config keys (`Slack:BotToken`, `Slack:SigningSecret`); locally they come from user-secrets. No bespoke secret delivery.

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

- None outstanding for this change.

**Resolved:** channels are **public** by default (private per-item override deferred); the first
provisioning trigger is **explicit request** (auto-on-active-status deferred); channel naming is
**`<jira-key>-<short-summary-slug>`**, Slack-normalized with the key preserved.
