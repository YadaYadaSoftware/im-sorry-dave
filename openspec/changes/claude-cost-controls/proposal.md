## Why

Every `/post` (and the TUI smoke path) calls the Anthropic Messages API through
`AnthropicDecisionExtractor` to summarize a conversation. This is now a live deployed system on
ECS, billed per token against a real Anthropic key — and the model defaults to `claude-opus-4-8`
($5 / $25 per 1M input/output tokens). Nothing today caps how many extraction calls fire, how many
tokens they consume, or what the month costs. A busy channel, a redelivery loop, or a misbehaving
client can quietly run up unbounded spend, and we have no counters to even see it happening. We need
budgets, caps, and visibility before the bill, not after.

## What Changes

- **Per-channel and per-day call caps.** Cap the number of extraction calls per channel per day and
  the total extraction calls per day; once a cap is hit, extraction is refused (no Claude call) with
  a clear in-Slack message rather than silently spending.
- **Global monthly token/cost budget.** Track input/output tokens (and estimated USD) for the
  calendar month against a configured budget, with a **soft** threshold (model downgrade) and a
  **hard** threshold (refuse-or-defer).
- **Model downgrade over the soft threshold.** When monthly spend crosses the soft threshold,
  transparently downgrade the model used for extraction (e.g. `claude-opus-4-8` → `claude-haiku-4-5`)
  to keep the platform running cheaply instead of stopping.
- **Refuse-or-defer over the hard threshold.** When a hard budget (monthly cost or a daily call cap)
  is exceeded, refuse the extraction and post a clear in-Slack message explaining the budget state;
  the `PostCursor` is left unadvanced so the same window retries once budget frees up.
- **Spend / usage visibility.** Record per-call token usage and running daily/monthly counters in EF
  (SQLite), and expose a read-only usage/spend report endpoint under `/admin`.
- **A budgeting decorator around `IDecisionExtractor`.** All enforcement lives in a decorator that
  wraps the real extractor in DI, so every call site (the `/post` summarizer and the TUI smoke path)
  is covered without touching `AnthropicDecisionExtractor` or call sites.

## Capabilities

### New Capabilities
- `claude-cost-controls`: budgets and caps on Claude/Anthropic extraction usage — per-channel and
  per-day call caps, a global monthly token/cost budget with soft-threshold model downgrade and
  hard-threshold refuse-or-defer, per-call usage recording, and a usage/spend report endpoint.

## Impact

- `src/SorryDave.JiraSync.Core/Summarization` — new `BudgetingDecisionExtractor : IDecisionExtractor`
  wrapping `AnthropicDecisionExtractor`; an `IClaudeUsageRecorder` (EF-backed) that reads
  `usage.input_tokens` / `usage.output_tokens` from the Anthropic response and increments counters.
- `src/SorryDave.JiraSync.Core/Configuration` — new `CostOptions` (section `Cost`) for caps,
  thresholds, the downgrade model, and per-1M token prices; `AnthropicOptions` unchanged.
- `src/SorryDave.JiraSync.Core/Domain` + `Persistence` — new `ClaudeUsageDay` / `ClaudeUsageMonth`
  (or one counter entity) and an EF migration; `JiraSyncDbContext` gains the `DbSet`(s).
- `src/SorryDave.JiraSync.Core/DependencyInjection/ServiceCollectionExtensions.cs` — register the
  decorator over the extractor (real or fake) when cost controls are enabled.
- `src/SorryDave.JiraSync.Api/Endpoints` — new read-only `/admin/cost` usage/spend report endpoint.
- No change to Slack endpoints under `/slack`; over-budget messaging reuses the existing Slack
  post path. Secrets (`ApiKey`) remain SSM-sourced; single ECS instance, so counters are
  process-local + EF-persisted (no cross-instance contention).
