## Why

A channel's conversation (or a pasted block of meeting notes) often contains real action items that
should become Jira issues, but today there is no path from "we discussed it in Slack" to "it's
tracked in Jira." The platform already captures messages (`CapturedMessage`), summarizes with Claude
(`IDecisionExtractor`), posts interactive Block Kit candidate cards, and writes confirmed records
through an idempotent outbox. The missing piece is turning the conversation into **new Jira issues**:
`IJiraClient` can comment on issues but cannot create them. This change closes that gap so a human
can trigger extraction, review the candidates, and create the issues with one confirm.

## What Changes

- **Trigger.** Add a channel command (`/triage`, or `/actions` with optional pasted text) that runs
  extraction over the recent captured conversation (or the pasted notes) for that channel.
- **Extract action items.** Reuse the Claude seam (`IDecisionExtractor`, gated on `Anthropic:ApiKey`
  with the fake fallback) to produce candidate action items, each with a **title**, **description**,
  and **issue type**.
- **Confirm via Block Kit.** Present each candidate as an interactive card (the existing
  Confirm/Reject pattern handled at `/slack/interactivity`) so a human confirms or rejects per item.
- **Create in Jira on confirm.** Add a **new create-issue capability** to `IJiraClient` and create
  the confirmed issue through the idempotent outbox / `IWriteBackService`, linked back to the
  originating work item / channel via `IMappingStore` where one exists, attributed to the confirming
  Slack user.
- **Idempotent create.** Carry a dedupe key (marker) so re-confirming the same candidate does not
  double-create; on re-confirm the system reports the already-created issue.

## Capabilities

### New Capabilities
- `conversation-to-work-items`: trigger extraction of action items from a channel conversation (or
  pasted notes), confirm candidates via Block Kit, and idempotently create the confirmed items as new
  Jira issues linked back to the originating work item / channel and attributed to the confirming
  user.

## Impact

- `src/SorryDave.JiraSync.Core/Jira` — **new** `CreateIssueAsync` on `IJiraClient` (project key,
  issue type, summary, description; returns the new issue key) plus the implementation; this is net-new
  (the client currently only get/search/add+edit comment).
- `src/SorryDave.JiraSync.Core/Slack` — handle the `/triage` (`/actions`) slash command; render
  candidate cards reusing the Block Kit confirm/reject pattern; route confirm/reject at
  `/slack/interactivity`.
- Claude seam — reuse `IDecisionExtractor` (gated on `Anthropic:ApiKey` + fake) to extract candidate
  action items from the conversation / pasted text.
- `IWriteBackService` / outbox — create the confirmed issue idempotently (dedupe marker); attribute to
  the confirming user + Slack source; back-link via `IMappingStore`.
- Config (SSM) — default project key and issue-type mapping for created issues.
