## Why

The platform deliberately provisioned Slack channels **lazily, on explicit request**, to avoid
channel sprawl. That decision is now reversed: the team wants a channel **created automatically when
a new Jira work item is created**, so discussion can start immediately with no manual step. The
channel should also pull in the right people from the start — the **item creator** and anyone
**@mentioned in the description** — and its welcome message should carry the item's **title and
description** so members have context on arrival.

## What Changes

- **Auto-provision on creation.** When a new tracked, eligible work item is created
  (`jira:issue_created`), the system provisions its channel automatically. Explicit
  provision/archive/unarchive stay available (now usually a no-op / for pre-existing items). Issue
  type still gates eligibility (`Slack:EligibleIssueTypes`) so the sprawl can be scoped. **BREAKING**
  reversal of the "lazy/explicit-only" requirement.
- **Invite the creator and description mentions.** On provision, invite the item's creator (Jira
  reporter) and every user **@mentioned in the description**, resolved through the existing identity
  resolver (config-map now; email later), skipping unresolved — in addition to the fixed
  `InviteUserIds` and the assignee already invited.
- **Welcome message with title + description.** The channel's initial context message includes the
  Jira **title** and **description** (currently it omits the description).
- **Plumbing:** capture the **reporter accountId** and the **mentioned accountIds** from the Jira
  issue (parse ADF `mention` nodes in the description) onto the work item; fire the change seam on
  **creation** (today it only fires on update) so the Slack service can react.

## Capabilities

### Modified Capabilities
- `slack-channel-lifecycle`: provisioning trigger becomes **automatic on work-item creation**
  (explicit still supported); channel **membership** extends to the creator and description mentions;
  the **welcome message** includes the title and description.

## Impact

- `src/SorryDave.JiraSync.Core/Jira` — `JiraIssueParser` (extract reporter accountId + description
  `mention` accountIds), `JiraIssueData` (+ `ReporterAccountId`, `MentionedAccountIds`).
- `src/SorryDave.JiraSync.Core/Domain/WorkItem` (+ the two fields) and a new EF migration.
- `src/SorryDave.JiraSync.Core/Sync/WorkItemSyncService` — notify the change listener on the
  **Created** path (it currently returns without notifying).
- `src/SorryDave.JiraSync.Core/Slack/SlackChannelService` — auto-provision on creation; invite
  creator + mentioned users; include the description in `BuildContext`.
- **Conflict resolution (from the spec review):** only `slack-channel-lifecycle` has a real
  conflict (handled here). `deployment-playbook` (proposed) needs a doc note that channels now
  auto-provision (its `Slack provision` smoke step still works, idempotently).
  `slack-conversation-summarization` is compatible but sees **more** channels/mappings (volume note).
  `github-work-item-linking` and `openspec-jira-linking` are **improved** — a linked channel is now
  guaranteed to exist. `azure-deployment` and `console-control-app` are unaffected.
