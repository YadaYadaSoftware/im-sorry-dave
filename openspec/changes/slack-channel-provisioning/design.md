## Context

Conversations need a home. We give each tracked Jira work item a dedicated Slack channel and keep that channel linked to the work item via the core mapping store. This change owns channel creation, lifecycle, membership, and context reflection. It depends on `jira-sync-core` for the work-item model, the mapping store, and Jira status-change events.

## Goals / Non-Goals

**Goals:**
- One Slack channel per tracked work item, provisioned and named deterministically.
- Lifecycle tied to work-item state: archive on close, unarchive on reopen.
- Durable, unique channel ↔ work-item link in the shared mapping store.
- Keep channel context (status, assignee, Jira link) current.

**Non-Goals:**
- Capturing or summarizing channel messages (that is `slack-conversation-summarization`).
- Writing anything back to Jira (that is `jira-decision-writeback`).
- Defining which items are "tracked" (owned by `jira-sync-core` scope config).

## Decisions

- **Archive, never delete.** Closed work items archive their channel to preserve history and stay within workspace norms; reopening unarchives. *Alternative:* delete on close — rejected (loses conversation history that may still be referenced).
- **Deterministic naming from the work-item key.** Channel name derives from the Jira key (e.g., `proj-1234-short-slug`) normalized to Slack rules, with a deterministic suffix on collision. Makes channels discoverable and idempotent to provision. *Alternative:* random names — rejected (undiscoverable, hard to dedupe).
- **Driven by Jira events from the core.** Provisioning and lifecycle react to work-item created/status-changed/assignee-changed signals emitted by `jira-work-item-sync`, rather than polling Slack. Keeps a single source of truth for state.
- **Identity resolution is best-effort.** Map Jira users to Slack users by email where available; if unresolved, continue without failing and record the skip. *Alternative:* hard-require mapping — rejected (would block provisioning).
- **Context reflected via topic/purpose + pinned message.** Status/assignee shown in topic; Jira link pinned. Low-noise updates; avoids spamming the channel on every minor change.

## Risks / Trade-offs

- [Slack channel-count / rate limits at scale] → Provision lazily (on first need), batch and back off on `rate_limited`, reuse archived channels on reopen.
- [Channel-name collisions across projects] → Include project + key in the derived name; deterministic suffix as fallback.
- [Noisy status updates] → Debounce/reflect only meaningful transitions (status, assignee), not every field change.
- [Orphaned channels if mapping store and Slack drift] → Periodic reconciliation of links against Slack channel state.
- [User cannot be invited (not in workspace)] → Skip and record; do not fail provisioning.

## Migration Plan

1. Create and configure the Slack App (bot scopes, event/install).
2. Implement provisioning + mapping; dry-run against a test work item.
3. Backfill channels for already-tracked open work items.
4. Enable lifecycle (archive/unarchive) behind a flag; verify on a closed test item.
- *Rollback:* disable provisioning; existing channels remain (archived links preserved).

## Open Questions

- Should channels be public or private by default?
- Provision for all tracked issue types, or only some (e.g., Story/Task/Bug but not Sub-task/Epic)?
- Naming convention details (prefix, slug length) and whether to include the summary slug.
