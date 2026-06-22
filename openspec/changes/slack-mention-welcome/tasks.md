# Tasks

## 1. Capture the mention text

- [x] 1.1 Return the comment body text alongside accountIds from the Jira client (extend
      `GetCommentMentionsAsync` to `(accountIds, text)` via `AdfText.Flatten`, or add a sibling)
- [x] 1.2 Add `MentionContext : string?` to `WorkItemChange`
- [x] 1.3 Populate `MentionContext` on the comment path (`WebhookProcessor`) and the description-edit
      path (`WorkItemSyncService`, from the work item's description)

## 2. Welcome the invited mentionees

- [x] 2.1 In `SlackChannelService`, on a non-created change that invites mentioned users to a linked
      channel, post one welcome message addressed to the just-invited users (`<@U1> <@U2> …`)
      containing the truncated `MentionContext`; best-effort
- [x] 2.2 Do not post a duplicate welcome at auto-provision (the description message already carries
      the text); only the comment / description-edit paths post the mention welcome

## 3. Tests

- [x] 3.1 Comment mention → invitee invited and welcomed with the comment text
- [x] 3.2 Description-edit mention → invitee welcomed with the description text
- [x] 3.3 Auto-provision with a description mention → no duplicate welcome posted

## 4. Deploy & verify

- [ ] 4.1 Deploy; add a comment mentioning a non-member user on an item with a channel → confirm they
      are invited and a welcome message with the comment text is posted
- [x] 4.2 Docs: note the mention-welcome message in the README Slack section
