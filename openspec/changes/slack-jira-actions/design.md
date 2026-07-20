## Context

Jira is the source of truth. The platform mirrors work items, provisions a Slack channel per item,
captures conversation, and writes decisions/answers back to Jira through an idempotent outbox. The
existing seams are already in place: `IJiraClient` (get/search/add+edit comment),
`IWriteBackService.SubmitAsync` (idempotent on `WorkItemKey`+`RecordIdentity`),
`IMappingStore.ResolveByResourceAsync(ResourceType.SlackChannel, channelId)` for the channel↔work-item
link, Slack endpoints under `/slack` (signature-verified; slash at `/slack/commands`, interactivity
at `/slack/interactivity`), and `WebhookProcessor` which already handles `comment_created`. This
change closes the loop in both directions using those seams without new infrastructure.

> **Correction (2026-07-20).** An earlier revision of this paragraph claimed `IJiraClient` already had
> assignee and status mutations. It does not: the interface has exactly two write methods,
> `AddCommentAsync` and `UpdateCommentAsync`. The `/assign` and `/status` flows below therefore require
> **adding** assignee-update and transition methods to `IJiraClient`, `JiraRestClient`, and
> `FakeJiraClient` — work this change's task list must account for and currently does not.
>
> **Build commands as plugins.** Since `slack-command-plugins` landed, slash commands are not wired into
> `SlackEventEndpoints` — every command implements `ISlackCommandPlugin`, owns its own interactivity
> actions under namespaced action ids, and is served only when named in the `Slack:EnabledCommands`
> allow-list. `/assign`, `/status`, and `/comment` should be authored against that contract rather than
> the endpoint. Note that all three are Jira **writes**, which are currently not permitted.

## Goals / Non-Goals

**Goals:**
- From a work-item channel, `/assign @user`, `/status <name>`, and `/comment <text>` mutate the
  linked Jira work item and report the result in-channel.
- Inbound Jira comments on a linked work item are posted to the channel (author + text).
- Platform-authored comments (carrying the `[managed-record:…]` marker) are not reflected, so no
  feedback loop forms.

**Non-Goals:**
- Interactive Block Kit dialogs / modals (interactivity at `/slack/interactivity` is out of scope
  here; commands are plain slash commands).
- Acting on items with no linked channel, or from non-work-item channels.
- Backfilling historical Jira comments into channels.
- Changing the write-back outbox contract or the webhook sync pipeline shape.

## Decisions

### Resolve the channel to a work item, then dispatch by command
**Decision:** `/slack/commands` (already signature-verified) parses the command name and text. The
channel id is resolved with `IMappingStore.ResolveByResourceAsync(ResourceType.SlackChannel,
channelId)`. If no mapping exists, reply ephemerally that the channel is not linked and stop. On a
hit, dispatch:
- `/assign @user` → resolve the Slack/Jira identity of `@user` to a Jira accountId (existing identity
  resolution), then `IJiraClient` assignee update on the resolved `WorkItemKey`.
- `/status <name>` → `IJiraClient` status transition; match `<name>` against the item's available
  transitions, transition if found, else reply with the valid transition names.
- `/comment <text>` → `IWriteBackService.SubmitAsync` so the comment goes through the idempotent
  outbox like every other platform-authored comment (so it carries the managed-record marker).

*Why route comments through the outbox, not a direct `IJiraClient.AddComment`:* the outbox is
idempotent on `WorkItemKey`+`RecordIdentity` and stamps the `[managed-record:…]` marker — which is
exactly the marker the reflection path uses to skip platform comments, so a `/comment` will not echo
back into the channel.

### Report the result in the channel
**Decision:** Each command replies in the channel with the outcome (e.g. "Assigned MDP-9 to
<@U…>", "MDP-9 → In Progress", "Comment added to MDP-9"). Failures (unresolved user, invalid
status, Jira error) reply ephemerally with a short reason. Best-effort: a failed channel post never
fails the command ack.

### Reflect inbound Jira comments into the linked channel
**Decision:** Extend the `WebhookProcessor` `comment_created` path. After the existing handling,
resolve the work item's channel via `IMappingStore` (work-item→channel). If a channel exists and the
comment is **not** a managed record, post the comment to the channel as author + text
(e.g. "💬 *Jane Doe* on MDP-9: <text>"). If there is no linked channel, do nothing.

### Skip managed records to prevent loops
**Decision:** Identify platform-authored comments by the `[managed-record:…]` marker the write-back
outbox stamps. The reflection path skips any comment whose body contains that marker, so a
`/comment` (which goes through the outbox) is written to Jira, fires `comment_created`, and is then
**not** reflected back — closing the loop. *Why marker-based and not author-based:* the marker is
already the canonical "the platform wrote this" signal and is robust to which bot/service account
authored it.

### Signature verification and best-effort posture
**Decision:** Reuse the existing `/slack` signature verification; no command bypasses it. All
channel posts (command results and comment reflection) are best-effort — they log and continue on
failure rather than failing the command ack or the webhook processing.

## Risks / Trade-offs

- [Comment reflection loop] → skip comments carrying `[managed-record:…]`; `/comment` routes through
  the outbox so it is always marked. Mitigated by design.
- [Slack 3-second slash-command timeout] → ack immediately; apply the Jira mutation and post the
  result asynchronously / report via the channel, so a slow Jira call does not time out the command.
- [Ambiguous `/status` names] → match against the item's available transitions; on no match, reply
  with the valid names rather than guessing.
- [Unresolvable `@user` for `/assign`] → reply ephemerally that the user could not be resolved; do
  not change the assignee.
- [Comment text formatting / length] → post author + flattened text, truncate defensively.

## Migration Plan

1. Add slash-command handling at `/slack/commands` for `/assign`, `/status`, `/comment`: resolve
   channel→work-item via `IMappingStore`, dispatch to `IJiraClient` / `IWriteBackService`, report in
   channel.
2. Wire `/assign` identity resolution to the existing Slack↔Jira identity seam.
3. Extend `WebhookProcessor.comment_created` to reflect non-managed comments into the linked channel.
4. Tests: each command applies the right Jira mutation and reports; `/comment` does not echo back;
   inbound human comment is reflected; managed-record comment is skipped; unlinked channel is a
   no-op.
5. Deploy; from a work-item channel run each command and add a Jira comment to confirm both
   directions.

## Open Questions

- Should `/status` accept transition ids in addition to names? Start with name matching; add ids if
  needed.
- Reflect comment edits/deletes too, or only `comment_created`? Scope here is `comment_created`;
  revisit edits later.
