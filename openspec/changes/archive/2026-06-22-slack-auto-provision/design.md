## Context

`slack-channel-provisioning` (archived) provisions channels **lazily, on explicit request**, and its
`SlackChannelService` already does most of what we need: create + link + seed context + invite (fixed
list, assignee, reporter) via a pluggable identity resolver. This change flips the **trigger** to
automatic-on-creation and extends membership + the welcome message. A full spec review found the only
real conflict is the lazy-provisioning requirement in main `slack-channel-lifecycle`; other active
changes are compatible (and the GitHub/OpenSpec linking changes are *improved* by a guaranteed
channel). The work is mostly reuse: the new pieces are a creation-time trigger, capturing the creator
+ description-mention accountIds, and adding the description to the welcome message.

## Goals / Non-Goals

**Goals:**
- A channel is created automatically when a new eligible work item is created.
- The creator (reporter) and everyone @mentioned in the description are invited (resolvable only).
- The welcome message includes the Jira title and description.

**Non-Goals:**
- Removing explicit provisioning or the lifecycle/reconciliation behavior (all retained).
- Resolving Slack identities by email on Jira Cloud (still blocked; config-map/invite-list as today).
- Auto-provisioning for *every* status/type unconditionally — `EligibleIssueTypes` still gates it.

## Decisions

### Trigger: auto-provision on `jira:issue_created`, explicit still works
**Decision:** Fire the existing change seam on the **Created** path of `WorkItemSyncService.ApplyIssueAsync`
(today it saves and returns without notifying). Extend `WorkItemChange` with a `Created` flag (or add
a sibling notification) and have `SlackChannelService` auto-provision on creation for eligible types.
Explicit `ProvisionAsync` is unchanged and idempotent (returns `AlreadyLinked`). *Why:* reuses the
best-effort listener seam (failures already swallowed, can't block Jira mirroring). *Alternative:*
trigger from the webhook handler on `SyncOutcome.Created` — equivalent; the listener seam keeps it in
one place and covers reconciliation/backfill creates too. *Latency note:* provisioning makes Slack
API calls inside the webhook request (~1–2s); acceptable, but keep it best-effort and consider
fire-and-forget if it proves slow.

### Sprawl is accepted but scoped by `EligibleIssueTypes`
**Decision:** Auto-provision only for issue types in `Slack:EligibleIssueTypes` (empty = all). *Why:*
the original lazy decision existed to avoid a channel per draft idea; eligibility is the knob to keep
that in check (e.g., set it to `Idea` rather than every type). The playbook should recommend scoping
it. *Trade-off:* on `MDP` (all "Parking lot" ideas) leaving it empty creates a channel per idea —
the team is opting into that; document it.

### Capture the creator and description mentions
**Decision:** Extend `JiraIssueParser` to also read `fields.reporter.accountId` and to walk the
**description ADF** for `mention` nodes, collecting their `attrs.id` (accountId). Add
`ReporterAccountId : string?` and `MentionedAccountIds : List<string>` to `JiraIssueData` and
`WorkItem` (the list uses the same value-converter pattern as `Labels`), with one EF migration adding
two nullable/text columns. *Why:* invites resolve off accountId (most reliable for the config-map and
the future email resolver); mentions only exist in the ADF, not the flattened text. *Note:* `creator`
vs `reporter` — use **reporter** (the creator in the common case; the issue's `reporter` is what we
already parse) and capture its accountId.

### Welcome is two messages: a pinned title+link, then the description
**Decision:** On provision, post **two** messages instead of one combined context message:
1. A header message with the item **title** (and key/type/status) and a **link to Jira** — this
   message is **pinned**.
2. A second message containing the **description** (skipped/short-noted if empty).

`SlackChannelService.SeedContextAsync` already posts a message and pins it; it becomes: post (1) →
pin (1) → post (2). `BuildContext` splits into `BuildHeader` (title + link) and the description body;
truncate the description defensively for very long values. *Why:* keeps the pinned reference compact
(title + link always visible) while the description reads as normal channel content.

### Invite creator + mentions on provision
**Decision:** `SeedContextAsync` passes the reporter (`JiraUserRef(ReporterAccountId,
ReporterDisplayName, null)`) and the mentioned users (`JiraUserRef(accountId, null, null)` each) to
`InviteParticipantsAsync`, which already invites the fixed list + resolved participants and skips
unresolved. The assignee is still invited as today.

### Conflict resolution across active changes
- **`slack-channel-lifecycle` (main spec):** the only real conflict — MODIFIED here (trigger,
  membership, welcome message).
- **`deployment-playbook` (proposed):** add a note that channels auto-provision; its `Slack provision`
  smoke step still works (idempotent). A small doc edit when that change is applied — not blocking.
- **`slack-conversation-summarization` (proposed):** compatible; it sees *more* channels/mappings —
  a volume note, no design change.
- **`github-work-item-linking`, `openspec-jira-linking` (proposed):** *improved* — they post to "the
  work item's Slack channel," which is now guaranteed to exist for eligible items.
- **`azure-deployment`, `console-control-app` (proposed):** no impact.

## Risks / Trade-offs

- [Channel sprawl] → gated by `EligibleIssueTypes`; archive-on-close still prunes; the hybrid
  channel-per-epic model remains the escape hatch (noted in the archived design).
- [Slack rate limits on bursty creation] → the client already backs off on `429`; creates are
  per-item, best-effort.
- [Webhook latency from inline provisioning] → best-effort; consider async if needed.
- [ADF mention parsing fragility] → defensive parse (missing/odd nodes skipped); mentions are
  best-effort like all invites.
- [Migration] → additive nullable columns; safe forward migration on the existing SQLite/EFS (and
  Azure Files later).

## Migration Plan

1. Parser + model: capture `ReporterAccountId` + `MentionedAccountIds`; EF migration for the columns.
2. Sync seam: notify on Created.
3. Slack service: auto-provision on creation; description in welcome; invite creator + mentions.
4. Tests: parser (reporter accountId + mention extraction), created→provision, invite creator +
   mentions, welcome includes description.
5. Deploy; verify a newly created MDP item auto-creates a channel with the creator invited and the
   description in the welcome message.
- *Rollback:* set `EligibleIssueTypes` to an unused type (or disable the created-notification) to
  suppress auto-provisioning; explicit provisioning still works.

## Open Questions

- Use `reporter` as "creator", or capture the distinct `creator` field too? Defaulting to reporter;
  revisit if they diverge in practice.
