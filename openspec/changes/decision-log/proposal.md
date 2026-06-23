## Why

Confirmed decisions, answers, and summaries are already written back to Jira as managed comments and
captured durably as `WriteBackRecord` rows (WorkItemKey, Kind, Content, RecordIdentity, SourceUrl,
Author, Status). But that data is write-only from the team's point of view: there is no way to ask
"what did we decide on MDP-7?" or "what did we decide about checkout?" without scrolling Jira. The
records already exist — they just need a query surface from Slack and the console/API so the team can
search the decision history.

## What Changes

- **Decision-log query API.** Add a read-only API over the existing `WriteBackRecord` store that
  lists and searches **confirmed** records — returning, for each, the decision text, the work item
  (with a `/browse/{key}` link built from `JiraOptions.BaseUrl`), the Slack source (`SourceUrl` /
  channel via `IMappingStore`), the confirming user (`Author`), the kind, and the timestamp.
- **Keyword filtering + pagination.** Filter by work item key, kind (Decision/Answer/Summary), and a
  free-text substring over the content; page results so large histories stay bounded.
- **`/decisions` Slack command.** A slash command that runs the same query — `/decisions MDP-7`
  (work-item scoped) or `/decisions <keywords>` (text) — and replies with the matching confirmed
  decisions, each with its Jira browse link and source.
- **Optional Claude-backed semantic search (additive).** When `Anthropic:ApiKey` is configured,
  `IDecisionExtractor` powers a semantic mode ("what did we decide about checkout?"); when the key is
  absent (or the fake is wired), the command and API fall back to keyword search with no error.

## Capabilities

### New Capabilities

- `decision-log`: a durable, searchable log of confirmed decisions/answers/summaries across work
  items, queryable from Slack and the console/API, backed by the existing `WriteBackRecord` data with
  optional Claude-backed semantic search.

## Impact

- `src/SorryDave.JiraSync.Core/WriteBack` — add read/query methods over the `WriteBackRecord` SQLite
  store (filter by WorkItemKey/Kind/text, status = confirmed, paged); no new store, no schema change
  to the record shape.
- `src/SorryDave.JiraSync.Core/DecisionLog` (new) — query service that joins `WriteBackRecord` with
  `IMappingStore` (Slack channel) and `JiraOptions.BaseUrl` (`/browse/{key}`) to build result rows;
  optional `IDecisionExtractor`-backed semantic ranking gated on `Anthropic:ApiKey`.
- Slack endpoints under `/slack` — register the `/decisions` command handler.
- Console/API + smoke TUI (an API client) — surface list/search of the decision log.
