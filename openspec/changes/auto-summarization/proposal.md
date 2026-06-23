## Why

v1 summarization triggers on the `/post` slash command only — a human must remember to run it. Useful
context decays: by the time someone runs `/post`, a busy channel has scrolled and the relevant window
is harder to recall. Two ergonomic, opt-in triggers were deliberately deferred from v1: summarize a
channel after it goes quiet (idle), and summarize on demand by reacting to a message (a cue). Both can
ride the existing `IConversationSummarizer.PostAsync` / candidate-card / confirm loop without changing
the write-back contract — confirmation still gates every Jira write.

## What Changes

- **Idle-timeout trigger (opt-in, off by default).** A background sweep finds linked work-item
  channels whose newest captured message is older than `IdleSummarizeAfter` and that have new
  conversation since the `PostCursor`, and runs the same summarize → candidate-card flow `/post` runs.
- **Reaction-cue trigger (opt-in, off by default).** Reacting to a message with a configured emoji
  (e.g. `:memo:`) on a linked channel summarizes the window around that message. Requires subscribing
  `/slack/events` to `reaction_added` (adds the `reactions:read` scope).
- **Human confirmation unchanged.** Both triggers post the same interactive Block Kit candidate cards;
  nothing is written to Jira until a human confirms. No trigger ever writes unsolicited.
- **Throttled and cost-bounded.** Per-channel cooldown plus de-dup against the `PostCursor` so an auto
  run and a manual `/post` (or two auto runs) never double-summarize the same window and a busy channel
  can't run up Claude spend.
- **Config to enable/scope.** New `SlackOptions` fields enable each trigger globally and constrain by
  channel/issue-type; both default off so existing deployments are unaffected.

## Capabilities

### New Capabilities
- `auto-summarization`: optional automatic triggers (idle-timeout sweep and reaction cue) that start
  the existing summarize → confirm → write-back loop, both opt-in, throttled, and gated by human
  confirmation.

## Impact

- `src/SorryDave.JiraSync.Core/Configuration/SlackOptions` — add idle/reaction enable flags, idle
  timeout, sweep interval, per-channel cooldown, reaction emoji, and channel/issue-type scoping.
- `src/SorryDave.JiraSync.Core/Summarization` — new `AutoSummarizeBackgroundService` (idle sweep,
  following the `SlackReconciliationBackgroundService` pattern); a small trigger gate that enforces
  cooldown + `PostCursor` de-dup before calling `PostAsync`.
- `src/SorryDave.JiraSync.Api/Endpoints/SlackEventEndpoints` — handle `reaction_added` on
  `/slack/events` (signature-verified already), mapping the cue to a summarize run.
- `src/SorryDave.JiraSync.Core/DependencyInjection/ServiceCollectionExtensions` — register the hosted
  service when the idle trigger is enabled.
- Slack app config: add `reaction_added` event subscription and `reactions:read` scope.
- Builds on the existing `IConversationSummarizer` loop and `PostCursor`; no domain schema change.
