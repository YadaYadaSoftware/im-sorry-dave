# Tasks

## 1. Slash-command plumbing

- [ ] 1.1 In the `/slack/commands` handler (signature-verified), parse the command name and text for
      `/assign`, `/status`, `/comment`
- [ ] 1.2 Resolve the channel to a work item via
      `IMappingStore.ResolveByResourceAsync(ResourceType.SlackChannel, channelId)`; on no mapping,
      reply that the channel is not linked and stop
- [ ] 1.3 Ack within Slack's timeout, then apply the action and report the result in the channel
      (best-effort)

## 2. Apply commands to Jira

- [ ] 2.1 `/assign @user`: resolve the user to a Jira accountId via identity resolution, then set the
      assignee via `IJiraClient`; reply if unresolvable without changing the assignee
- [ ] 2.2 `/status <name>`: match `<name>` against the item's available transitions and transition via
      `IJiraClient`; on no match, reply with the valid transition names
- [ ] 2.3 `/comment <text>`: submit via `IWriteBackService.SubmitAsync` so it is idempotent and
      carries the `[managed-record:…]` marker

## 3. Reflect Jira comments into the channel

- [ ] 3.1 Extend the `WebhookProcessor` `comment_created` path: resolve the work item's linked
      channel via `IMappingStore`; if none, do nothing
- [ ] 3.2 Skip comments whose body carries the `[managed-record:…]` marker (loop prevention)
- [ ] 3.3 Post non-managed comments to the channel as author + text (best-effort)

## 4. Tests

- [ ] 4.1 `/assign` sets the assignee on identity resolve; unresolvable user → no change + reply
- [ ] 4.2 `/status` transitions on a matching name; non-matching name → valid names replied, no change
- [ ] 4.3 `/comment` submits through the outbox and does not echo back into the channel
- [ ] 4.4 Inbound human comment on a linked item is reflected; managed-record comment is skipped;
      unlinked item is a no-op

## 5. Deploy & verify

- [ ] 5.1 Deploy; from a work-item channel run `/assign`, `/status`, `/comment` and confirm each
      applies to Jira and reports in-channel
- [ ] 5.2 Add a human Jira comment on a linked item → confirm it appears in the channel and a
      `/comment` does not loop back
