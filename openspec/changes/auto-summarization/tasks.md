# Tasks

## 1. Configuration

- [ ] 1.1 Add `IdleSummarizeEnabled` (default false), `IdleSummarizeAfter`, `AutoSummarizeSweepInterval`,
      and `AutoSummarizeCooldown` to `SlackOptions`
- [ ] 1.2 Add `ReactionCueEnabled` (default false) and `ReactionCueEmoji` (default `memo`) to `SlackOptions`
- [ ] 1.3 Add scoping config (channel allow-list and/or issue-type gating reusing the existing
      `EligibleIssueTypes` convention) for which channels are eligible for auto triggers

## 2. Trigger gate (throttle + de-dup)

- [ ] 2.1 Add a per-channel cooldown gate that records last auto-trigger time and refuses to fire within
      `AutoSummarizeCooldown`; explicit `/post` bypasses it
- [ ] 2.2 Rely on `PostAsync`'s since-`PostCursor` window so auto and manual never double-summarize; no
      second cursor

## 3. Idle-timeout trigger

- [ ] 3.1 Add `AutoSummarizeBackgroundService : BackgroundService` following
      `SlackReconciliationBackgroundService`; register it only when `IdleSummarizeEnabled`
- [ ] 3.2 Each sweep, select linked channels whose newest non-deleted `CapturedMessage` is older than
      `IdleSummarizeAfter` and that have a `Ts` after `PostCursor.LastPostedTs`, honoring scope + cooldown
- [ ] 3.3 For each eligible channel call `IConversationSummarizer.PostAsync` and post the returned
      candidate cards as the `/post` handler does

## 4. Reaction-cue trigger

- [ ] 4.1 In `SlackEventEndpoints`, handle `reaction_added` in the `event_callback` branch; when enabled,
      the reaction equals `ReactionCueEmoji`, and the channel is linked, run summarization
- [ ] 4.2 Ack within 3s and run the extraction fire-and-forget in its own DI scope (mirror `/commands`),
      honoring scope + cooldown
- [ ] 4.3 Document the required Slack app `reaction_added` event subscription and `reactions:read` scope

## 5. Tests

- [ ] 5.1 Idle sweep: idle + unposted channel summarized; active channel and idle-but-empty channel skipped
- [ ] 5.2 Cooldown suppresses a rapid second auto-trigger; `/post` bypasses the cooldown
- [ ] 5.3 Reaction cue: configured emoji on a linked channel summarizes; wrong emoji and unlinked channel ignored
- [ ] 5.4 Auto and `/post` do not double-summarize the same window; rejected auto candidate leaves the cursor unchanged
- [ ] 5.5 Both triggers default off → explicit-only behavior unchanged

## 6. Deploy & verify

- [ ] 6.1 Add the `reaction_added` subscription and `reactions:read` scope to the Slack app
- [ ] 6.2 Deploy with triggers off; enable per-channel in a pilot and confirm candidate cards appear and
      still require human confirmation before write-back
- [ ] 6.3 Docs: note the idle and reaction triggers, config, and required scope in the README Slack section
