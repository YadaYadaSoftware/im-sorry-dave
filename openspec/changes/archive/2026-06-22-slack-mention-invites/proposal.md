## Why

`slack-auto-provision` invites users @mentioned in a work item's **description**, but only **at
channel creation**. In practice people @mention teammates in **comments** and in **later description
edits** to pull them into the discussion — and today nothing happens (the `comment_created` webhook
re-syncs the issue but never reads the comment body, and post-creation description mentions never
re-trigger invites). This closes that gap: a mention anywhere that matters invites the person to the
channel, whenever it happens. It also seeds the Jira→Slack `UserMap` so mentions actually resolve on
the deployed instance (currently empty, so every mention is skipped).

## What Changes

- **Comment mentions invite, ongoing.** When a `comment_created` / `comment_updated` webhook arrives
  for an item with a linked channel, parse the **comment body** for `@mention` accountIds and invite
  those users (resolved via the identity resolver; unresolved skipped).
- **Description mentions invite after creation too.** When an `issue_updated` adds new description
  mentions, invite them (not just at provision time).
- **Seed `UserMap`.** Add Chris Tacke's Jira accountId → his Slack id to the committed
  `appsettings.json` `Slack:UserMap`, so mention-invites resolve on the deployed instance.
- Mechanism: extend `WorkItemChange` with `MentionedAccountIds`; the `comment_*` webhook path and the
  description-mention-changed update path fire the change seam carrying those ids; `SlackChannelService`
  invites them (idempotent, best-effort) when a channel exists.

## Capabilities

### Modified Capabilities
- `slack-channel-lifecycle`: channel **membership** extends from "description @mentions at creation"
  to "description **and comment** @mentions, **ongoing**" — invites fire whenever a mention appears,
  for items that have a channel.

## Impact

- `src/SorryDave.JiraSync.Core/Sync/WebhookProcessor` — parse `root.comment.body` mentions on
  `comment_*` events and notify the change listeners with them (inject `IEnumerable<IWorkItemChangeListener>`).
- `src/SorryDave.JiraSync.Core/Sync/IWorkItemChangeListener` — add `MentionedAccountIds` to
  `WorkItemChange`.
- `src/SorryDave.JiraSync.Core/Sync/WorkItemSyncService` — on update, notify when the description
  mention set changes (carry current mentions); include mentions on the created notification.
- `src/SorryDave.JiraSync.Core/Slack/SlackChannelService` — on a non-created change with a linked
  channel, invite `MentionedAccountIds` (resolve, skip unresolved, idempotent).
- `src/SorryDave.JiraSync.Api/appsettings.json` — `Slack:UserMap` entry for Chris.
- No schema change (reuses `WorkItem.MentionedAccountIds` + the resolver). Builds on, and is scoped
  to the same `slack-channel-lifecycle` capability as, `slack-auto-provision`.
