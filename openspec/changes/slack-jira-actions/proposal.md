## Why

Today the Jira↔Slack loop is one-way: Jira state flows into Slack channels, but people in a
work-item channel cannot act on Jira from there, and Jira comments do not appear in the channel.
This makes the channel an incomplete mirror and forces context-switching back to Jira.

## What Changes

- **Slack slash commands act on the linked work item.** In a work-item channel, `/assign @user`,
  `/status <name>`, and `/comment <text>` resolve to the channel's linked work item and apply to
  Jira — assignee changes and status transitions via `IJiraClient`, comments via the write-back
  outbox (`IWriteBackService.SubmitAsync`) — with the result reported back in the channel.
- **Jira comments are reflected into the linked Slack channel.** When a `comment_created` webhook
  arrives for a work item with a linked channel, the comment (author + text) is posted to that
  channel so it mirrors the full Jira conversation.
- **Loop prevention.** Comments the platform itself wrote (identified by the `[managed-record:…]`
  marker) are skipped when reflecting, so platform-authored write-back comments never echo back.
- **Identity resolution for `/assign`.** The `@user` argument is resolved to a Jira accountId via
  the existing identity-resolution seam before the assignment is applied.

## Capabilities

### New Capabilities
- `slack-jira-actions`: two-way Jira↔Slack actions — channel slash commands that mutate the linked
  Jira work item (assign, status, comment) and reflection of inbound Jira comments into the linked
  channel, with managed-record loop prevention.

## Impact

- `src/SorryDave.JiraSync.Core/Slack` — slash-command handling at `/slack/commands`: parse
  `/assign`, `/status`, `/comment`; resolve the channel to a work item via
  `IMappingStore.ResolveByResourceAsync(ResourceType.SlackChannel, channelId)`; apply via
  `IJiraClient` / `IWriteBackService`; post the result back to the channel.
- `src/SorryDave.JiraSync.Core/Jira` — `IJiraClient` assignee update and status transition used by
  the slash commands.
- `src/SorryDave.JiraSync.Core/Sync` — `WebhookProcessor` `comment_created` path: when the item has
  a linked channel and the comment is not a managed record, post the comment to the channel.
- Identity resolution reused for `/assign`; signature verification reused on `/slack` endpoints. No
  schema change.
