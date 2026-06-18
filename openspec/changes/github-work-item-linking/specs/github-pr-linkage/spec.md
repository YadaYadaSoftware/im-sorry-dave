## ADDED Requirements

### Requirement: Detect work-item keys in GitHub activity

The system SHALL detect Jira work-item keys in pull request titles, pull request bodies, branch names, and commit messages, and SHALL associate the pull request with each detected work item.

#### Scenario: Key in PR title links the PR

- **WHEN** a pull request is opened whose title contains a valid Jira work-item key
- **THEN** the system links that pull request to the corresponding work item in the mapping store

#### Scenario: Key in branch or commit links the PR

- **WHEN** a pull request's branch name or a commit message contains a valid work-item key
- **THEN** the system links the pull request to that work item even if the title does not contain the key

#### Scenario: No key found

- **WHEN** no valid work-item key is present in the PR title, body, branch, or commits
- **THEN** the system does not link the pull request and records it as unlinked

#### Scenario: Unknown key

- **WHEN** a detected key does not correspond to a tracked work item
- **THEN** the system does not create a link and records the key as unresolved

### Requirement: Post PR visibility to Jira and Slack

The system SHALL post pull request lifecycle updates for a linked PR to the work item's Jira issue and its Slack channel.

#### Scenario: PR opened posted

- **WHEN** a linked pull request is opened
- **THEN** the system posts an update (with the PR link, author, and title) to the work item's Jira issue and to its Slack channel

#### Scenario: PR merged posted

- **WHEN** a linked pull request is merged
- **THEN** the system posts a merge update to the work item's Jira issue and Slack channel

#### Scenario: Updates are idempotent

- **WHEN** the same PR lifecycle event is delivered more than once
- **THEN** the system does not post duplicate updates

### Requirement: Maintain the PR ↔ work-item link

The system SHALL store the GitHub pull request ↔ work-item association in the core mapping store and keep it current as links change.

#### Scenario: Link recorded

- **WHEN** a pull request is linked to a work item
- **THEN** the association is recorded in the mapping store and resolvable in both directions

#### Scenario: Multiple PRs per work item

- **WHEN** more than one pull request references the same work item
- **THEN** the system links all of them to that work item
