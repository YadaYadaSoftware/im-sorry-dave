## ADDED Requirements

### Requirement: Per-channel and per-day extraction call caps

The system SHALL cap the number of Claude extraction calls per channel per day and the total number
of extraction calls per day, using configured limits (`MaxCallsPerChannelPerDay`, `MaxCallsPerDay`,
where 0 means unlimited). When a cap would be exceeded, the system SHALL refuse the extraction without
calling the Anthropic API and SHALL signal the refusal so the caller can post an over-budget message
and leave the channel's `PostCursor` unadvanced for retry.

#### Scenario: Per-channel daily cap reached

- **WHEN** a channel has already made its configured maximum number of extraction calls for the
  current UTC day and another `/post` triggers extraction for that channel
- **THEN** the system does not call the Anthropic API
- **AND** it returns no candidates and signals that the per-channel daily cap was reached

#### Scenario: Global daily cap reached

- **WHEN** the total number of extraction calls across all channels has reached the configured daily
  maximum and another extraction is requested
- **THEN** the system refuses the extraction without calling the Anthropic API and signals the global
  daily cap as the reason

#### Scenario: Under the caps

- **WHEN** neither the per-channel nor the global daily cap has been reached
- **THEN** the system performs the extraction and counts the call against both the per-channel and the
  global daily counters

### Requirement: Global monthly token and cost budget

The system SHALL track input and output tokens used by Claude extraction for the current UTC calendar
month and SHALL compute an estimated cost in USD from those token counts and the configured per-1M
token prices for the model used. It SHALL evaluate that running cost against a configured monthly
budget (`MonthlyBudgetUsd`) using a soft threshold and a hard threshold.

#### Scenario: Below the soft threshold

- **WHEN** the month's estimated cost is below the soft threshold
- **THEN** the system performs extraction normally using the configured primary model

#### Scenario: Monthly counters reset at the month boundary

- **WHEN** a new UTC calendar month begins
- **THEN** the monthly token and cost counters used for budget evaluation reflect only the new month

### Requirement: Model downgrade over the soft threshold

The system SHALL, when the month's estimated cost is at or above the configured soft threshold but
below the hard threshold, perform extraction using the configured downgrade model (`DowngradeModel`,
e.g. `claude-haiku-4-5`) instead of the primary model, and SHALL record usage and cost against the
model actually used.

#### Scenario: Soft threshold crossed downgrades the model

- **WHEN** the month's estimated cost is at or above the soft threshold but below the hard threshold
  and an extraction is requested
- **THEN** the system calls the Anthropic API using the configured downgrade model rather than the
  primary model
- **AND** it records the resulting token usage and estimated cost using the downgrade model's prices

#### Scenario: Downgrade is transparent to users

- **WHEN** extraction runs on the downgrade model
- **THEN** the system still produces a summary and posts it normally, with no over-budget message to
  the channel

### Requirement: Refuse-or-defer over the hard budget

When the month's estimated cost is at or above the configured hard threshold, the system SHALL refuse
extraction without calling the Anthropic API, SHALL post a clear in-Slack message in the affected
channel explaining that summarization is paused due to the budget, and SHALL leave the channel's
`PostCursor` unadvanced so the same conversation window is retried once budget is available.

#### Scenario: Hard budget exceeded refuses and notifies

- **WHEN** the month's estimated cost is at or above the hard threshold and a `/post` triggers
  extraction
- **THEN** the system does not call the Anthropic API
- **AND** it posts an in-Slack message in the channel stating that summarization is paused due to the
  monthly Claude budget
- **AND** it does not advance the channel's `PostCursor`

#### Scenario: Deferred window retries after budget frees up

- **WHEN** a window was deferred because the hard budget was exceeded and budget later becomes
  available (e.g. the new month resets the counters)
- **THEN** a subsequent `/post` re-attempts extraction over the same unadvanced window

### Requirement: Per-call usage recording

The system SHALL record, for each extraction call, the channel, the model used, the input and output
token counts reported by the Anthropic Messages API response (`usage.input_tokens`,
`usage.output_tokens`), and the estimated cost, persisting daily and monthly aggregates in EF
(SQLite) so they survive process restarts. When the underlying extractor returns no token usage (for
example the deterministic fake extractor), the system SHALL record the call with zero tokens.

#### Scenario: Successful Anthropic call records token usage

- **WHEN** an extraction call to the Anthropic API succeeds and the response includes a usage object
- **THEN** the system records the call, its input and output token counts, the model used, and the
  estimated cost against the daily and monthly counters

#### Scenario: Fake extractor records a zero-token call

- **WHEN** extraction is served by the deterministic fake extractor (no Anthropic key configured)
- **THEN** the system records one call with zero input and output tokens and zero estimated cost

#### Scenario: Counters persist across restarts

- **WHEN** the process restarts within the same UTC month
- **THEN** the previously recorded monthly token and cost totals are still reflected in budget
  evaluation

### Requirement: Usage and spend report

The system SHALL expose a read-only administrative report of Claude usage and spend that includes the
current day's and current month's call counts, input and output token totals, and estimated cost in
USD, along with the configured caps and budget.

#### Scenario: Report returns current usage and budget state

- **WHEN** an operator requests the usage/spend report endpoint
- **THEN** the system returns the current daily and monthly call counts, token totals, and estimated
  cost, together with the configured daily caps and monthly budget

### Requirement: Cost control enforcement covers all extraction call sites

The system SHALL enforce caps, budget, downgrade, and usage recording by decorating
`IDecisionExtractor` in dependency injection, so that every consumer of `IDecisionExtractor` — the
`/post` conversation summarizer and the TUI smoke path — is subject to the same controls without
changes to call sites or to `AnthropicDecisionExtractor`.

#### Scenario: /post path is governed

- **WHEN** a `/post` triggers extraction through `ConversationSummarizer`
- **THEN** the same cap, budget, downgrade, and recording rules apply

#### Scenario: TUI smoke path is governed

- **WHEN** the `/admin/summarize` smoke path triggers extraction
- **THEN** the same cap, budget, downgrade, and recording rules apply

#### Scenario: Controls disabled passes through

- **WHEN** cost controls are configured as disabled
- **THEN** the decorator performs no refusal or downgrade and only records usage
