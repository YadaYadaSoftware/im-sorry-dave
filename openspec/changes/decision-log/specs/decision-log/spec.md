## ADDED Requirements

### Requirement: Query the confirmed decision log

The system SHALL provide a read-only query over the existing `WriteBackRecord` store that returns only
**confirmed** records, exposing for each result the decision text (Content), the work item key, a Jira
browse link built as `{JiraOptions.BaseUrl}/browse/{key}`, the Slack source (the `SourceUrl` and the
channel resolved via `IMappingStore` when available), the confirming user (`Author`), the kind
(Decision/Answer/Summary), and the timestamp. The query SHALL NOT introduce a new persistence store or
modify the write-back path; it reads the existing records.

#### Scenario: List recent confirmed decisions

- **WHEN** the decision log is queried with no filter
- **THEN** the system returns the most recent confirmed `WriteBackRecord` rows, newest first
- **AND** each result includes the decision text, work item key, the `/browse/{key}` Jira link, the
  Slack source, the confirming user, the kind, and the timestamp

#### Scenario: Unconfirmed records are excluded

- **WHEN** the decision log is queried and some records have a non-confirmed status
- **THEN** the system returns only records whose status is confirmed and omits the rest

#### Scenario: Missing channel mapping falls back to the source URL

- **WHEN** a confirmed record's work item has no Slack channel in `IMappingStore`
- **THEN** the system still returns the record using its `SourceUrl` for the Slack source and does not
  fail the query

### Requirement: Keyword filtering and pagination

The decision log query SHALL support filtering by work item key, by kind (Decision/Answer/Summary),
and by a free-text substring over the decision content, and SHALL paginate results so that no single
response returns an unbounded history.

#### Scenario: Filter by work item key

- **WHEN** the decision log is queried with a work item key (e.g. `MDP-7`)
- **THEN** the system returns only confirmed records for that work item

#### Scenario: Filter by kind

- **WHEN** the decision log is queried with a kind filter
- **THEN** the system returns only confirmed records of that kind

#### Scenario: Free-text keyword filter

- **WHEN** the decision log is queried with a text term
- **THEN** the system returns only confirmed records whose content matches the term

#### Scenario: Results are paginated

- **WHEN** more confirmed records match a query than a single page holds
- **THEN** the system returns one bounded page and an indication that more results exist

### Requirement: Decisions Slack command

The system SHALL register a `/decisions` slash command under the `/slack` endpoints that runs the
decision-log query and replies with the matching confirmed decisions, each showing its text, the
`/browse/{key}` Jira link, the Slack source, the confirming user, and the timestamp. When the command
argument matches a work item key, the query SHALL be scoped to that work item; otherwise the argument
SHALL be treated as free text.

#### Scenario: Work-item-scoped command

- **WHEN** a user runs `/decisions MDP-7`
- **THEN** the system replies with the confirmed decisions for `MDP-7`, each with its Jira browse link
  and Slack source

#### Scenario: Free-text command

- **WHEN** a user runs `/decisions checkout`
- **THEN** the system replies with the confirmed decisions whose content matches the term

#### Scenario: No matches

- **WHEN** a `/decisions` query has no matching confirmed records
- **THEN** the system replies that no decisions were found rather than erroring

### Requirement: Optional Claude-backed semantic search

The system SHALL provide an optional semantic search mode over the confirmed decision log, powered by
`IDecisionExtractor`, that is gated on the `Anthropic:ApiKey` configuration. When the key is configured
the system MAY answer natural-language queries (e.g. "what did we decide about checkout?") using
semantic matching; when the key is absent or the fake extractor is wired, the system SHALL fall back to
keyword search without error.

#### Scenario: Semantic search enabled

- **WHEN** `Anthropic:ApiKey` is configured and a natural-language decision query is run
- **THEN** the system uses `IDecisionExtractor` to return semantically relevant confirmed decisions

#### Scenario: Semantic search disabled falls back to keyword search

- **WHEN** `Anthropic:ApiKey` is not configured (or the fake extractor is in use) and a query is run
- **THEN** the system returns keyword-matched confirmed decisions without error
