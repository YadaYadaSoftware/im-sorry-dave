## Why

The team plans work as OpenSpec changes (proposal/design/specs/tasks) but tracks it in Jira. Today those live in separate worlds, so spec status isn't visible in Jira and engineers hand-create Jira items that drift from the specs. We want OpenSpec changes linked to Jira work items, with spec status surfaced in Jira/Slack, and the ability to generate or update Jira work items directly from OpenSpec change proposals and their tasks.

## What Changes

- Associate an OpenSpec change (and its capabilities/specs) with one or more Jira work items, recorded in the core mapping store.
- Surface OpenSpec artifact and task status (e.g., proposal/design/specs/tasks completeness, apply-readiness) onto the linked Jira issue and its Slack channel.
- Generate Jira work items from an OpenSpec change: a parent item for the change and child items from `tasks.md` task groups/tasks, with idempotent updates as the change evolves.
- Keep generated items in sync: re-running generation reconciles existing items rather than duplicating them, and reflects task completion back to Jira where appropriate.

## Capabilities

### New Capabilities
- `openspec-spec-linkage`: Link OpenSpec changes/specs to Jira work items and surface OpenSpec status onto Jira and Slack.
- `openspec-item-generation`: Create and idempotently update Jira work items (parent + tasks) from an OpenSpec change.

### Modified Capabilities
<!-- None - new capabilities. Depends on jira-sync-core (model, mapping, write/transition). -->

## Impact

- Reads OpenSpec change artifacts via the `openspec` CLI (`status`/`show ... --json`) and/or the `openspec/changes` files.
- Depends on `jira-work-item-sync` (mapping + model), `jira-decision-writeback`/Jira client (issue create + update + transition), and `slack-jira-linkage` (channel posts).
- Introduces a generation policy (which task granularity becomes Jira issues, parent/child issue types) and an idempotency key tying OpenSpec artifacts to Jira items.
- Must avoid fighting Jira authority: generation creates/updates items but Jira remains the source of truth for human-edited fields.
