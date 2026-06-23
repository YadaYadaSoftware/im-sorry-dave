## Context

The platform summarizes conversations by calling the Anthropic Messages API through
`AnthropicDecisionExtractor : IDecisionExtractor`, configured by `AnthropicOptions` (`ApiKey`,
`Model` defaulting to `claude-opus-4-8`, `MaxWindowMessages`, `RedactionPatterns`). `IDecisionExtractor`
is resolved in `ServiceCollectionExtensions.AddJiraSyncCore` — the real extractor when an Anthropic
key is configured, a `FakeDecisionExtractor` otherwise — and is consumed by `ConversationSummarizer`
on every `/post` and by the `/admin/summarize` TUI smoke path. The per-channel `PostCursor` only
advances on a successful write-back, so a refused or failed post naturally retries the same window.

`AnthropicDecisionExtractor.ExtractAsync` already parses the Anthropic JSON response but currently
discards the `usage` object the API returns (`usage.input_tokens`, `usage.output_tokens`). EF
persistence is SQLite via `JiraSyncDbContext`; the system runs as a single ECS instance, so counters
do not need distributed coordination — process-local state backed by EF is sufficient.

Real Claude is billable: `claude-opus-4-8` is $5 / $25 per 1M input/output tokens;
`claude-haiku-4-5` is $1 / $5 per 1M. Nothing today bounds call volume or spend.

## Goals / Non-Goals

**Goals:**
- Cap extraction calls per channel per day and per day overall.
- Enforce a global monthly token/cost budget with a soft threshold (model downgrade) and a hard
  threshold (refuse-or-defer).
- Make over-budget behavior visible to users via a clear in-Slack message, and to operators via a
  usage/spend report.
- Cover every call site by decorating `IDecisionExtractor` in DI — no call-site edits.

**Non-Goals:**
- Cross-instance / distributed budget coordination (single ECS instance; revisit if we scale out).
- Exact billing reconciliation against Anthropic invoices — we record estimated cost from token
  counts and configured prices, not authoritative billing.
- Changing the extraction prompt, window sizing (`MaxWindowMessages`), or redaction.
- Rate-limiting Slack endpoints or Jira sync.

## Decisions

### Enforce in a decorator around `IDecisionExtractor`
**Decision:** Add `BudgetingDecisionExtractor : IDecisionExtractor` that wraps the inner extractor
(real or fake). On each `ExtractAsync`, it (1) checks daily/per-channel call caps and the monthly
hard budget, (2) refuses (returns no candidates) and signals the over-budget reason when a hard cap
is hit, (3) chooses the model — inner model normally, downgrade model when over the soft threshold,
(4) delegates to the inner extractor, and (5) records the resulting token usage. Register it in
`AddJiraSyncCore` so it wraps whichever inner extractor is chosen.

*Why a decorator:* every call site resolves `IDecisionExtractor`; wrapping it covers `/post` and the
TUI smoke path without touching `AnthropicDecisionExtractor`, `ConversationSummarizer`, or any
endpoint. *Model selection:* the inner extractor reads `AnthropicOptions.Model`; to downgrade, the
decorator must influence the model. Simplest seam: have the decorator pass the chosen model to the
inner extractor (a small overload / context), or set it on a scoped per-call value the inner reads.
Prefer a per-call model parameter threaded through `IDecisionExtractor.ExtractAsync` over mutating
shared options.

### Where counters live (EF)
**Decision:** Persist counters in SQLite via `JiraSyncDbContext`. Use day-scoped and month-scoped
counter rows: `ClaudeUsageDay { DateUtc, ChannelId?, Calls, InputTokens, OutputTokens }` keyed for
both per-channel-per-day and all-channels-per-day aggregates, and a monthly aggregate
(`ClaudeUsageMonth { MonthUtc, InputTokens, OutputTokens, EstimatedCostUsd }`) — or derive the month
by summing days. Increment within the same `DbContext` scope as the call. Single instance means no
write contention; a brief in-memory cache can front the DB for cap checks, with EF as the source of
truth across restarts.

*Why EF/SQLite:* matches the existing `PostCursor` / `CapturedMessage` persistence; survives ECS task
restarts so the monthly budget isn't reset by a redeploy.

### How token/cost is estimated and recorded
**Decision:** Read actual `usage.input_tokens` / `usage.output_tokens` from the Anthropic response —
the most accurate source — via a small `IClaudeUsageRecorder` the decorator calls after a successful
inner call. Estimated USD = `(input/1e6)*InputPricePerMTok + (output/1e6)*OutputPricePerMTok`, using
prices from `CostOptions` keyed by the model actually used (opus vs haiku). When the inner extractor
returns no usage (fake extractor, or a failed call that returned no candidates), record zero tokens
and one call. Prices are config, not hard-coded, so a price change is a deploy-free SSM/config edit.

### Config additions (`CostOptions`)
**Decision:** Add `CostOptions` (section `Cost`), separate from `AnthropicOptions` so cost policy is
orthogonal to the API client config:
- `Enabled` (default true; false = decorator passes through and only records).
- `MaxCallsPerChannelPerDay`, `MaxCallsPerDay` (hard call caps; 0 = unlimited).
- `MonthlyBudgetUsd`, `SoftThresholdPercent` (e.g. 80), `HardThresholdPercent` (e.g. 100).
- `DowngradeModel` (e.g. `claude-haiku-4-5`).
- `InputPricePerMTokUsd` / `OutputPricePerMTokUsd` per model (a small map, or paired values for the
  primary + downgrade models).

### Over-budget UX (refuse vs defer)
**Decision:** When a hard cap/budget is exceeded, the decorator returns **no candidates** and surfaces
an over-budget signal so the summarizer skips the write-back; because `PostCursor` only advances on a
successful post, the window is effectively **deferred** and retried next time budget allows. The
summarizer posts a clear in-Slack message (reusing the existing Slack post path) such as
`⚠️ Summary paused: monthly Claude budget reached (resets <date>).` or
`⚠️ Summary paused: this channel hit its daily summarization limit.` Soft-threshold downgrade is
silent to users (it still produces a summary, just on the cheaper model) but is visible in the report.

## Risks / Trade-offs

- [Estimated cost ≠ actual invoice] → record from real response token counts + configured prices;
  label it "estimated" in the report. Accept small drift vs Anthropic billing.
- [Counter races on the single instance] → single ECS instance + EF transaction per call keeps it
  simple; if we scale out, move to a shared store or per-instance budgets (Non-Goal today).
- [Downgrade reduces summary quality near month-end] → it's a deliberate trade (cheap summary >
  no summary); operators see it in the report and can raise the budget.
- [Refuse could starve a busy channel] → defer-and-retry (cursor unadvanced) means the window isn't
  lost; per-channel caps stop one channel from consuming the global budget.
- [Inner model threading] → adding a per-call model to `ExtractAsync` touches the interface; keep it
  optional/defaulted so the fake and existing callers are unaffected.

## Migration Plan

1. Add `CostOptions` and register `Configure<CostOptions>` in `AddJiraSyncCore`.
2. Add EF counter entities + `DbSet`s and a migration; `IClaudeUsageRecorder` implementation.
3. Surface `usage` token counts out of `AnthropicDecisionExtractor` (return them, or record via the
   recorder) and thread an optional per-call model into `IDecisionExtractor.ExtractAsync`.
4. Add `BudgetingDecisionExtractor` and register it as the `IDecisionExtractor` wrapping the inner
   (real/fake) extractor.
5. Wire the over-budget signal into `ConversationSummarizer` so it posts the in-Slack message and
   leaves the cursor unadvanced.
6. Add the read-only `/admin/cost` report endpoint.
7. Deploy; verify caps refuse with a Slack message, soft threshold downgrades the model, and the
   report shows daily/monthly tokens and estimated cost.

## Open Questions

- Soft/hard thresholds as percent-of-budget vs absolute USD — start with percent of `MonthlyBudgetUsd`.
- Month boundary in UTC vs a billing-aligned timezone — start with UTC calendar month.
- Whether to also cap the TUI smoke path or exempt it (it exercises real Claude) — default: count it
  but allow an exemption flag if smoke testing trips the cap.
