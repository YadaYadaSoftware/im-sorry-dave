## 1. OpenSpec status access

- [ ] 1.1 Implement an OpenSpec reader using the `openspec` CLI (`status`/`show --json`)
- [ ] 1.2 Add artifact/task parsing fallback from `openspec/changes/<name>` files
- [ ] 1.3 Model change status: artifacts complete, apply-readiness, task completion

## 2. Linkage

- [ ] 2.1 Record OpenSpec change-name ↔ work-item associations in the core mapping store
- [ ] 2.2 Expose bidirectional resolution (change → work items, work item → change)

## 3. Status surfacing

- [ ] 3.1 Reflect OpenSpec status onto the linked Jira issue (via core write path)
- [ ] 3.2 Post OpenSpec status updates to the work item's Slack channel
- [ ] 3.3 Ignore status changes for unlinked changes

## 4. Item generation

- [ ] 4.1 Define the generation policy (granularity, parent/child issue types) and config
- [ ] 4.2 Create the parent Jira item from the change proposal and link it
- [ ] 4.3 Create child items from `tasks.md` at the configured granularity, linked to parent and task
- [ ] 4.4 Implement a stable idempotency key tying each item to its OpenSpec source

## 5. Reconciliation

- [ ] 5.1 Re-run generation to update existing items and add only new tasks (no duplicates)
- [ ] 5.2 Flag/close items for tasks removed from `tasks.md` per policy
- [ ] 5.3 Optionally reflect task completion via valid Jira transitions, preserving human-owned fields

## 6. Validation

- [ ] 6.1 Add a dry-run mode that reports planned creates/updates without writing
- [ ] 6.2 Unit tests for idempotency key, granularity policy, and human-edit preservation
- [ ] 6.3 Integration test: generate from a sample change → re-run is idempotent → added task creates one item

## 7. Console commands

- [ ] 7.1 Provide `openspec link <change> <key>` and `openspec status <change>` handlers
- [ ] 7.2 Provide `openspec generate <change>` handler with `--dry-run` preview support
