# Tasks

## 1. Capture creator + description mentions (parser & model)

- [x] 1.1 `JiraIssueParser`: also read `fields.reporter.accountId`, and walk the description ADF for
      `mention` nodes, collecting their `attrs.id` (accountId)
- [x] 1.2 Add `ReporterAccountId : string?` and `MentionedAccountIds : List<string>` to `JiraIssueData`
- [x] 1.3 Add the same fields to `WorkItem` (the list via the `Labels` value-converter pattern) and
      thread them through `WorkItemSyncService` (both Created and Updated assignments)
- [x] 1.4 Add an EF migration for the two new columns

## 2. Fire the change seam on creation

- [x] 2.1 Add a `Created` flag (or sibling notification) to `WorkItemChange`
- [x] 2.2 In `WorkItemSyncService.ApplyIssueAsync`, notify listeners on the **Created** path (today it
      saves and returns without notifying), best-effort like the update path

## 3. Auto-provision + invites + two-message welcome (`SlackChannelService`)

- [x] 3.1 On a created notification, auto-provision the channel for eligible issue types (reuse
      `ProvisionAsync`; idempotent)
- [x] 3.2 Two welcome messages: post a **header** (title + Jira link) and **pin** it; post a second
      message with the **description** (skip/short-note if empty); set the topic as today
- [x] 3.3 Invite the **creator** (`ReporterAccountId`/`ReporterDisplayName`) and each **mentioned**
      user (`MentionedAccountIds`) via the identity resolver, alongside the assignee + watcher list;
      skip unresolved

## 4. Tests

- [x] 4.1 Parser: extracts `ReporterAccountId` and the description `mention` accountIds
- [x] 4.2 Created → auto-provision (eligible only); out-of-scope type → no channel
- [x] 4.3 Welcome: header (title + link) is pinned; a separate description message is posted
- [x] 4.4 Invites: creator + mentioned users invited (resolved via config-map); unresolved skipped

## 5. Deploy & verify

- [ ] 5.1 Deploy; create a new eligible MDP item in Jira and confirm a channel auto-provisions with
      the pinned title+link message, a description message, and the creator invited

## 6. Docs & conflict follow-ups

- [x] 6.1 Document auto-provisioning + the `EligibleIssueTypes` scoping recommendation (README /
      `INSTRUCTIONS.md`); when `deployment-playbook` is applied, note that channels auto-provision and
      the manual `Slack provision` step is now optional/idempotent
