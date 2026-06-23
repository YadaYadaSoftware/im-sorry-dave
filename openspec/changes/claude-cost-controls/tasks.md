# Tasks

## 1. Configuration

- [ ] 1.1 Add `CostOptions` (section `Cost`) with `Enabled`, `MaxCallsPerChannelPerDay`,
      `MaxCallsPerDay`, `MonthlyBudgetUsd`, `SoftThresholdPercent`, `HardThresholdPercent`,
      `DowngradeModel`, and per-model `InputPricePerMTokUsd` / `OutputPricePerMTokUsd`
- [ ] 1.2 Register `services.Configure<CostOptions>(...)` in `AddJiraSyncCore`

## 2. Usage persistence (EF)

- [ ] 2.1 Add counter entities (`ClaudeUsageDay` keyed for per-channel-per-day and all-channels-per-day,
      plus a monthly aggregate) and add `DbSet`(s) + `OnModelCreating` config to `JiraSyncDbContext`
- [ ] 2.2 Add an EF migration for the new counter tables
- [ ] 2.3 Add `IClaudeUsageRecorder` + EF implementation that increments daily/monthly counters and
      estimated cost from token counts and configured prices

## 3. Surface token usage from the extractor

- [ ] 3.1 Read `usage.input_tokens` / `usage.output_tokens` from the Anthropic response in
      `AnthropicDecisionExtractor` (return them or record via the recorder)
- [ ] 3.2 Thread an optional per-call model into `IDecisionExtractor.ExtractAsync` (defaulted so the
      fake and existing callers are unaffected)

## 4. Budgeting decorator

- [ ] 4.1 Add `BudgetingDecisionExtractor : IDecisionExtractor` wrapping the inner extractor:
      check per-channel/daily call caps and the monthly hard threshold; refuse with a reason when
      exceeded
- [ ] 4.2 Select the model (primary normally, `DowngradeModel` at/above the soft threshold) and pass
      it to the inner extractor
- [ ] 4.3 Record usage after a successful inner call (zero tokens when the fake serves it)
- [ ] 4.4 Register the decorator as `IDecisionExtractor` over the real/fake inner extractor in
      `AddJiraSyncCore`; pass through (record only) when `Enabled` is false

## 5. Over-budget UX

- [ ] 5.1 In `ConversationSummarizer`, when extraction is refused for budget/caps, post a clear
      in-Slack message and leave the channel `PostCursor` unadvanced (defer-and-retry)

## 6. Usage/spend report

- [ ] 6.1 Add a read-only `/admin/cost` endpoint returning current daily/monthly call counts, token
      totals, estimated cost, and the configured caps/budget

## 7. Tests

- [ ] 7.1 Per-channel and global daily caps refuse without an Anthropic call
- [ ] 7.2 Soft threshold downgrades the model and records cost at the downgrade prices
- [ ] 7.3 Hard threshold refuses, posts the Slack message, and leaves the cursor unadvanced
- [ ] 7.4 Usage recorded from a successful response; fake extractor records a zero-token call
- [ ] 7.5 Monthly counters reset at the UTC month boundary and persist across a restart

## 8. Deploy & verify

- [ ] 8.1 Deploy; drive a channel past its daily cap and confirm the in-Slack pause message
- [ ] 8.2 Lower the soft threshold and confirm the report shows extraction on the downgrade model
- [ ] 8.3 Confirm `/admin/cost` reports daily/monthly tokens and estimated cost
