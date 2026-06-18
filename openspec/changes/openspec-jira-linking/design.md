## Context

The team plans in OpenSpec and tracks in Jira. This change bridges them: link OpenSpec changes to Jira work items, surface OpenSpec status into Jira/Slack, and generate/update Jira items from OpenSpec changes and their tasks. It depends on `jira-sync-core` (model, mapping, issue create/update/transition) and `slack-jira-linkage` (channel posts), and reads OpenSpec via its CLI/artifacts.

## Goals / Non-Goals

**Goals:**
- A durable link between an OpenSpec change and its Jira work item(s).
- OpenSpec artifact/task status visible on the linked Jira issue and Slack channel.
- Idempotent generation of a Jira parent + task children from an OpenSpec change.
- Generation that respects Jira as the source of truth for human-owned fields.

**Non-Goals:**
- Editing OpenSpec artifacts from Jira (one-directional: OpenSpec → Jira for generation).
- Replacing the OpenSpec workflow or CLI.
- Owning Jira write/transition primitives (reuses the core Jira client).

## Decisions

- **OpenSpec is authoritative for plan structure; Jira is authoritative for tracking fields.** Generation creates/updates items from specs/tasks but never overwrites human-owned Jira fields (assignee, status set by people, edited descriptions). Avoids the two systems fighting. *Alternative:* full two-way sync — rejected (conflict-prone, out of scope).
- **Read status from the OpenSpec CLI.** Use `openspec status/show ... --json` (and artifacts as fallback) as the authoritative source for artifact/task completeness and apply-readiness. *Alternative:* parse markdown ad hoc — kept only as fallback.
- **Stable idempotency key.** Each generated Jira item carries a stable identity derived from (change name, artifact/task locator) stored in the mapping store and an issue marker, so regeneration updates in place and additions create only new items. Prevents duplication as `tasks.md` evolves.
- **Configurable generation granularity.** Policy decides whether task groups or individual tasks become issues, and the parent/child issue types. Teams structure backlogs differently. *Alternative:* fixed mapping — rejected (not portable).
- **Removed tasks are flagged/closed, not deleted.** Honors Jira history and avoids destroying tracked work. *Alternative:* delete — rejected.
- **Status reflection is opt-in and transition-valid.** Task completion may advance a child item only through a valid Jira transition when the policy enables it; otherwise generation is content-only. Keeps automation safe against arbitrary workflows.

## Risks / Trade-offs

- [Duplicate items on regeneration] → Stable idempotency key + reconciliation by mapping store.
- [Overwriting human edits in Jira] → Field ownership rules; regeneration touches only generated/safe fields.
- [tasks.md churn creating backlog noise] → Configurable granularity; default to task-group level; flag (not delete) removed tasks.
- [OpenSpec CLI output changes across versions] → Pin to `--json` contracts; fall back to artifacts; tolerate missing fields.
- [Status reflection conflicting with GitHub status automation] → Both go through valid Jira transitions; document precedence (human > GitHub merge > OpenSpec task completion).

## Migration Plan

1. Implement linkage + status surfacing (read-only toward Jira content/comments first).
2. Add generation in dry-run (report what would be created) and validate against a sample change.
3. Enable generation behind a flag; create parent + children for one pilot change; verify idempotent re-run.
4. Optionally enable status reflection with a conservative policy.
- *Rollback:* disable generation/reflection flags; existing links and items remain.

## Open Questions

- Default generation granularity (task groups vs. individual tasks) and parent/child issue types.
- Whether generation is triggered manually (command), on change apply-readiness, or on a schedule.
- Precedence rules when GitHub status automation and OpenSpec task-completion reflection both target the same item.
