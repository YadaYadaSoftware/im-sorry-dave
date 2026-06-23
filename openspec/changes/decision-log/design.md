## Context

Jira is the source of truth. Confirmed decisions/answers/summaries are written back to Jira as
managed comments through an idempotent outbox, and each is persisted as a `WriteBackRecord`
(WorkItemKey, Kind = Decision/Answer/Summary, Content, RecordIdentity, SourceUrl, Author, Status) in
the SQLite store (EF), and also as a `SummaryCandidate`. That store already *is* the decision history
— it is just never read back for the team. This change adds a read path: a query service over the
existing records plus a `/decisions` Slack command and console/API access. The seams it leans on
already exist: the `WriteBackRecord` store (SQLite/EF), `IMappingStore` (work-item ↔ Slack channel),
`JiraOptions.BaseUrl` for `/browse/{key}` links, `IDecisionExtractor` (Claude, gated on
`Anthropic:ApiKey` + fake) for the optional semantic mode, and the Slack endpoints under `/slack`.

## Goals / Non-Goals

**Goals:**

- A read-only query API over confirmed `WriteBackRecord` rows: list and search across work items.
- Each result row shows the decision text, the work item (with a `/browse/{key}` link), the Slack
  source, the confirming user, the kind, and the timestamp.
- Keyword filtering by work item, kind, and free text; paginated results.
- A `/decisions` Slack command and console/API surface for the same query.
- Optional Claude-backed semantic search, additive and gated on `Anthropic:ApiKey`.

**Non-Goals:**

- No new persistence store and no change to the `WriteBackRecord` write path / outbox — read only.
- No editing/deleting of decisions from the log.
- No new full-text/vector index infrastructure (semantic mode reuses `IDecisionExtractor`, not a DB).
- Not changing how decisions are confirmed or written back to Jira.

## Decisions

### Reuse `WriteBackRecord` as the log (no duplicate store)

**Decision:** Treat the existing `WriteBackRecord` rows as the decision log and add query methods to
its store rather than building a separate read model or index. Filter to `Status` = confirmed, expose
filters for `WorkItemKey`, `Kind`, and a substring over `Content`, ordered newest-first and paged.

*Why:* the records are already durable, idempotent, and authoritative; a second store would have to
be kept in sync and could drift from Jira (the source of truth). *Alternative — dedicated read
model/index:* rejected for now as duplicative; if query volume or text-search needs outgrow SQLite
`LIKE`, a projection can be added later behind the same query interface without changing callers.

### A `DecisionLog` query service that composes the seams

**Decision:** Add a query service that takes a filter (work item, kind, text, paging) and returns
result rows. Each row is built by joining a `WriteBackRecord` with: `JiraOptions.BaseUrl` →
`/browse/{key}` for the Jira link; `IMappingStore` → the Slack channel for the source (alongside
`SourceUrl`); and the record's `Author` / timestamp for the confirming user and when. Both the Slack
command and the console/API call this one service so results are identical across surfaces.

### `/decisions` Slack command shape

**Decision:** Register `/decisions` under `/slack`. If the argument matches a work-item key pattern
(e.g. `MDP-7`), scope the query to that `WorkItemKey`; otherwise treat the argument as free text.
Reply with the matching confirmed decisions (decision text + `/browse/{key}` link + source +
confirming user + timestamp), paginated — show the first page and indicate more. Empty argument lists
recent confirmed decisions.

### Pagination

**Decision:** Page at the store level (limit + offset / cursor) so neither the API nor the Slack
reply materializes an unbounded history. The Slack command shows one page with a "more" affordance;
the API exposes page/size (or cursor) parameters and a total/next indicator.

### Optional semantic search is additive and gated

**Decision:** Keyword search is always available. When `Anthropic:ApiKey` is configured, a semantic
mode uses `IDecisionExtractor` to rank/match confirmed records against a natural-language query
("what did we decide about checkout?"). When the key is absent or the fake extractor is wired, the
service falls back to keyword search and never errors. *Why additive:* semantic search is a quality
boost, not a dependency; the log must work without Claude. Same gating pattern as elsewhere
(`Anthropic:ApiKey` + fake).

## Risks / Trade-offs

- [SQLite `LIKE` text search is limited] → acceptable for current volume; the query interface allows a
  later index/projection without changing callers.
- [Large histories] → mitigated by mandatory pagination at the store level.
- [Stale Slack channel mapping] → fall back to `SourceUrl` when `IMappingStore` has no channel for the
  work item; never fail the query over a missing mapping.
- [Semantic mode latency/cost] → gated on `Anthropic:ApiKey`, opt-in per query, and always degradable
  to keyword search.
- [Confirming-user attribution] → use the record's `Author`; if absent, show the decision without a
  confirmer rather than failing.

## Migration Plan

1. Add read/query methods to the `WriteBackRecord` store (filter by WorkItemKey/Kind/text, confirmed
   only, paged); no schema change.
2. Add the `DecisionLog` query service composing `IMappingStore` + `JiraOptions.BaseUrl` into result
   rows; expose it on the console/API.
3. Register the `/decisions` Slack command under `/slack` that calls the same service.
4. Add the optional `IDecisionExtractor`-backed semantic mode behind the `Anthropic:ApiKey` gate, with
   keyword fallback (covered by the fake).
5. Tests: keyword filter by work item / kind / text; pagination; browse-link and source composition;
   semantic mode on (fake) and gated-off fallback.

## Open Questions

- Result ordering for semantic mode — relevance score vs. recency tiebreaker; start with relevance,
  fall back to newest-first for keyword mode.
- Whether `/decisions` with no argument should default to the current channel's work item (via
  `IMappingStore`) or to a global recent list — start with global recent.
