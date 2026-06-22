## Context

`slack-auto-provision` invites description @mentions at channel creation via the
`IWorkItemChangeListener` seam (Core fires change events; `SlackChannelService` reacts, keeping Core
decoupled from Slack). Two gaps remain: comment mentions are never read (the `comment_created` path
in `WebhookProcessor` calls `ApplyIssueAsync` on the issue and ignores `comment.body`), and
description mentions added after creation never re-invite. This change extends the same seam to carry
mentions and fires it from the comment path and the description-changed update path. `AdfText.CollectMentionAccountIds`
(built in `slack-auto-provision`) already extracts mention accountIds from any ADF node — reused for
comment bodies.

## Goals / Non-Goals

**Goals:**
- A description **or comment** @mention invites the person to the item's channel, **whenever** it
  happens (not only at creation).
- Keep Core decoupled from Slack (reuse the change-listener seam).
- Make mention-invites actually resolve on the deployed instance (seed `UserMap`).

**Non-Goals:**
- Inviting on mentions for items that have **no** channel (e.g. ineligible types) — out of scope;
  nothing to invite into.
- Removing/auto-removing members when a mention is deleted.
- Resolving identities by email (still config-map only on Jira Cloud).

## Decisions

### Carry mentions on `WorkItemChange`; fire from comment + description-changed paths
**Decision:** Add `MentionedAccountIds : List<string>` to `WorkItemChange`. Three producers:
- **Created** (existing): description mentions already invited in `SeedContextAsync` — unchanged.
- **Comment** (`comment_created`/`comment_updated`): `WebhookProcessor` parses `root.comment.body`
  via `AdfText.CollectMentionAccountIds`, then notifies a change carrying those ids.
- **Description edit** (`issue_updated`): `WorkItemSyncService` notifies when the description mention
  set changes (in addition to the existing status/assignee triggers), carrying the current mentions.

`SlackChannelService.OnWorkItemChangedAsync`: for a **non-created** change with a linked channel,
invite `MentionedAccountIds` (resolve via the chain, skip unresolved, idempotent). No channel → no-op
(consistent with how status/assignee reflection already behaves).

*Why this seam:* it's the established Core→Slack decoupling; comments and description edits become
just more producers of the same event. *Alternative — call Slack directly from the webhook handler:*
rejected (couples Core to Slack, which the listener seam exists to avoid).

### `WebhookProcessor` gets the listeners and parses comment bodies
**Decision:** Inject `IEnumerable<IWorkItemChangeListener>` into `WebhookProcessor` (same pattern as
`WorkItemSyncService`). On a `comment_*` event: keep the existing `ApplyIssueAsync(issue)` (re-syncs
the issue), then if `root.comment.body` has mentions, notify the listeners with a `WorkItemChange {
Key, Status = data.Status, MentionedAccountIds = commentMentions }`. Best-effort (listener failures
already swallowed). *Why:* the comment body only exists in the webhook root, not in the issue the sync
service sees. *Note:* notifying from two places (sync service + webhook processor) is fine — listeners
are idempotent and best-effort; document it.

### Invites stay idempotent and best-effort
**Decision:** Reuse `InviteParticipantsAsync` semantics — `ISlackClient.InviteAsync` already treats
`already_in_channel`/`cant_invite_self` as success, so re-inviting on every comment is harmless.
Unresolved mentions are logged and skipped.

### Seed `UserMap` for Chris (committed config)
**Decision:** Add `"5ba2a06b9477482ee0fac25f": "UA83GA928"` (Chris Tacke's Jira accountId → Slack id)
to `appsettings.json` `Slack:UserMap`. *Why:* the deployed map is empty, so without it every mention
resolves to nothing. Slack user ids aren't secrets — committed config is the right home (consistent
with `slack-auto-provision`). Further people are added the same way.

## Risks / Trade-offs

- [Invite spam on busy items] → idempotent invites + per-comment scope; already-in-channel is a
  no-op. Acceptable.
- [Double notification (sync + webhook on a comment)] → listeners idempotent/best-effort; the comment
  notification only adds mention invites, the issue re-sync handles status/assignee as before.
- [Mention of a non-Jira user] → can't happen (Jira only mentions real users); unmapped users are
  skipped.
- [No channel for the item] → mentions are simply not actioned; matches existing behavior.

## Migration Plan

1. Add `MentionedAccountIds` to `WorkItemChange`; populate on the created + description-changed paths.
2. Inject listeners into `WebhookProcessor`; parse comment-body mentions and notify on `comment_*`.
3. `SlackChannelService`: invite `MentionedAccountIds` on non-created changes with a linked channel.
4. Add the Chris `UserMap` entry to `appsettings.json`.
5. Tests: comment-mention parse + invite; description-edit adds a mention → invite; unresolved
   skipped; no-channel no-op.
6. Deploy; add a comment mentioning Chris on an item with a channel → Chris invited.
- *Rollback:* drop the `UserMap` entry (mentions skip) and/or stop notifying from the comment path.

## Open Questions

- None. (Removing members on un-mention is intentionally out of scope.)
