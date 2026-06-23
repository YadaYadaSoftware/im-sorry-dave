# Tasks

## 1. Query over the WriteBackRecord store

- [ ] 1.1 Add read/query methods to the `WriteBackRecord` SQLite/EF store that return confirmed-only
      records, filterable by WorkItemKey, Kind, and a free-text substring over Content, ordered
      newest-first
- [ ] 1.2 Add pagination (limit + offset/cursor) and a "more results" indicator; no schema change

## 2. DecisionLog query service

- [ ] 2.1 Add a `DecisionLog` query service that maps each confirmed `WriteBackRecord` to a result row
- [ ] 2.2 Build the Jira browse link as `{JiraOptions.BaseUrl}/browse/{key}`
- [ ] 2.3 Resolve the Slack source via `IMappingStore`, falling back to `SourceUrl` when no channel is
      mapped
- [ ] 2.4 Include the confirming user (`Author`), the kind, and the timestamp on each row

## 3. Slack command

- [ ] 3.1 Register the `/decisions` command under the `/slack` endpoints, calling the `DecisionLog`
      service
- [ ] 3.2 Scope to a work item when the argument matches a key (e.g. `MDP-7`); otherwise treat it as
      free text; reply with one paginated page and a "more" affordance

## 4. Console/API surface

- [ ] 4.1 Expose list/search of the decision log on the console/API using the same service
- [ ] 4.2 Confirm the smoke TUI (API client) can list/search confirmed decisions

## 5. Optional semantic search

- [ ] 5.1 Add an `IDecisionExtractor`-backed semantic mode gated on `Anthropic:ApiKey`
- [ ] 5.2 Fall back to keyword search when the key is absent or the fake extractor is wired (no error)

## 6. Tests

- [ ] 6.1 Keyword filters: by work item, by kind, by free text return confirmed-only records
- [ ] 6.2 Pagination bounds results and signals more
- [ ] 6.3 Result rows compose the `/browse/{key}` link and the Slack source (mapping + `SourceUrl`
      fallback)
- [ ] 6.4 Semantic mode returns relevant records with the fake extractor; gated-off path falls back to
      keyword search without error
- [ ] 6.5 `/decisions MDP-7` and `/decisions <text>` return the expected confirmed decisions
