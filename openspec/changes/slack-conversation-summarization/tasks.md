## 1. Slack event intake

- [ ] 1.1 Add Events API subscriptions (`message.channels`, `app_mention`) and the request endpoint
- [x] 1.2 Verify Slack request signatures and respond to the URL verification challenge
- [x] 1.3 De-duplicate redelivered events

## 2. Transcript capture

- [x] 2.1 Resolve the work item from the channel via `slack-jira-linkage`; ignore unlinked channels
- [x] 2.2 Store messages with author, timestamp, and thread parent
- [x] 2.3 Apply message edit/delete updates to the stored transcript

## 3. Claude extraction

- [x] 3.1 Add the Anthropic Claude API client and model config (default `claude-opus-4-8`, configurable to `claude-haiku-4-5`)
- [x] 3.2 Define structured-output schema for decision/answer/summary candidates with evidence + confidence
- [x] 3.3 Implement extraction over a conversation unit (thread or bounded channel window)
- [x] 3.4 Apply baseline redaction (on by default, configurable patterns) to both Claude inputs and Jira outputs

## 4. Confirmation & commands

- [ ] 4.1 Present candidates in Slack as interactive messages (confirm / edit / reject)
- [ ] 4.2 Implement the explicit summarize triggers: the **`/post`** slash command and the configured emoji-reaction cue
- [x] 4.3 Implement the `/post` window: a **per-channel cursor** of the last successful `/post`; window = messages since that cursor (whole conversation on first post); advance the cursor only on a successful write-back, never on a no-op/rejected/failed post
- [x] 4.4 Handle the command/confirmation in unlinked channels gracefully

## 5. Write-back integration

- [x] 5.1 Submit confirmed candidates to `jira-decision-writeback` with source + confirming-user attribution
- [ ] 5.2 Record open questions detected in Slack against the work item for answer tracking
- [ ] 5.3 Report write-back success/failure back into the originating channel

## 6. Cost control & validation

- [ ] 6.1 Bound conversation-unit size and throttle extraction calls to control Claude cost
- [x] 6.2 Unit tests for capture dedupe, candidate grounding, and idempotent confirmation
- [x] 6.3 Integration test: conversation → extraction → confirm → Jira record (no duplicate on re-confirm)

## 7. Console commands

- [x] 7.1 Provide a `summarize <key|channel> [--window]` handler that runs extraction and prints candidates
- [x] 7.2 Provide `candidates list <key>` and `candidates confirm/reject <id>` handlers
- [x] 7.3 Route console confirmations through `jira-decision-writeback`, honoring `--dry-run`
