# Tasks

## 1. Carry mentions on the change event

- [ ] 1.1 Add `MentionedAccountIds : List<string>` to `WorkItemChange`
- [ ] 1.2 `WorkItemSyncService`: include description mentions on the Created notification; on the
      Updated path, notify when the description mention set changes (not only status/assignee),
      carrying the current `MentionedAccountIds`

## 2. Comment-mention parsing & notification

- [ ] 2.1 Inject `IEnumerable<IWorkItemChangeListener>` into `WebhookProcessor`
- [ ] 2.2 On `comment_created` / `comment_updated`, after `ApplyIssueAsync`, parse `root.comment.body`
      with `AdfText.CollectMentionAccountIds`; if any, notify listeners with a `WorkItemChange`
      carrying those mention ids (best-effort)

## 3. Invite on mention (`SlackChannelService`)

- [ ] 3.1 In `OnWorkItemChangedAsync`, for a non-created change with a linked channel, resolve and
      invite `MentionedAccountIds` (idempotent; skip unresolved; no channel → no-op)

## 4. Seed UserMap

- [ ] 4.1 Add `"5ba2a06b9477482ee0fac25f": "UA83GA928"` (Chris Tacke) to `Slack:UserMap` in
      `appsettings.json`

## 5. Tests

- [ ] 5.1 Comment mention → invite (mapped user invited; unmapped skipped; no-channel no-op)
- [ ] 5.2 Description edit adding a mention → invite
- [ ] 5.3 `WebhookProcessor` parses comment-body mentions and notifies listeners

## 6. Deploy & verify

- [ ] 6.1 Deploy; add a comment mentioning Chris on an item with a channel → confirm Chris is invited
- [ ] 6.2 Docs: note comment/ongoing mention invites in the README Slack section
