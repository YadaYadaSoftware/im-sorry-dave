## ADDED Requirements

### Requirement: Generate Jira items from an OpenSpec change

The system SHALL generate Jira work items from an OpenSpec change: a parent item representing the change and child items derived from the change's `tasks.md`, according to a configurable generation policy.

#### Scenario: Parent item created for change

- **WHEN** generation runs for an OpenSpec change that has no linked parent work item
- **THEN** the system creates a Jira parent item populated from the change's proposal (title and summary) and links it to the change

#### Scenario: Child items created from tasks

- **WHEN** generation runs and the change's `tasks.md` defines task groups/tasks selected by the generation policy
- **THEN** the system creates corresponding Jira child items linked to the parent and to their originating tasks

#### Scenario: Generation respects policy granularity

- **WHEN** the generation policy specifies the task granularity that becomes Jira issues (e.g., task groups vs. individual tasks)
- **THEN** the system creates items only at the configured granularity

### Requirement: Idempotent generation and reconciliation

Re-running generation for the same OpenSpec change SHALL update existing linked items rather than create duplicates, using a stable identity tying each Jira item to its OpenSpec source.

#### Scenario: Re-run updates instead of duplicating

- **WHEN** generation runs again for a change whose items already exist
- **THEN** the system updates the existing parent/child items in place and creates only items for newly added tasks

#### Scenario: New task added later

- **WHEN** a new task is added to `tasks.md` after initial generation
- **THEN** a subsequent generation creates a child item for the new task without disturbing existing items

#### Scenario: Removed task handled per policy

- **WHEN** a task that previously produced a Jira item is removed from `tasks.md`
- **THEN** the system flags or closes the corresponding item per the generation policy rather than silently deleting it

### Requirement: Reflect task completion without overriding Jira authority

The system SHALL reflect OpenSpec task completion toward Jira where the generation policy allows, while never overwriting fields a human owns in Jira.

#### Scenario: Completed task reflected

- **WHEN** a task is marked complete in OpenSpec and the policy enables status reflection
- **THEN** the system advances the linked child item only via a valid Jira transition, leaving human-owned fields untouched

#### Scenario: Human edits preserved

- **WHEN** a generated item has been edited by a human in Jira
- **THEN** regeneration does not overwrite the human-edited authoritative fields
