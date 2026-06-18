## 1. Slack event intake

- [ ] 1.1 Add Events API subscriptions (`message.channels`, `app_mention`) and the request endpoint
- [ ] 1.2 Verify Slack request signatures and respond to the URL verification challenge
- [ ] 1.3 De-duplicate redelivered events

## 2. Transcript capture

- [ ] 2.1 Resolve the work item from the channel via `slack-jira-linkage`; ignore unlinked channels
- [ ] 2.2 Store messages with author, timestamp, and thread parent
- [ ] 2.3 Apply message edit/delete updates to the stored transcript

## 3. Claude extraction

- [ ] 3.1 Add the Anthropic Claude API client and model/effort configuration
- [ ] 3.2 Define structured-output schema for decision/answer/summary candidates with evidence + confidence
- [ ] 3.3 Implement extraction over a conversation unit (thread or bounded channel window)
- [ ] 3.4 Apply the redaction/consent policy to inputs and outputs

## 4. Confirmation & commands

- [ ] 4.1 Present candidates in Slack as interactive messages (confirm / edit / reject)
- [ ] 4.2 Implement the on-demand summarize slash command (thread/channel window)
- [ ] 4.3 Handle the command/confirmation in unlinked channels gracefully

## 5. Write-back integration

- [ ] 5.1 Submit confirmed candidates to `jira-decision-writeback` with source + confirming-user attribution
- [ ] 5.2 Record open questions detected in Slack against the work item for answer tracking
- [ ] 5.3 Report write-back success/failure back into the originating channel

## 6. Cost control & validation

- [ ] 6.1 Throttle/debounce automatic extraction and bound conversation-unit size
- [ ] 6.2 Unit tests for capture dedupe, candidate grounding, and idempotent confirmation
- [ ] 6.3 Integration test: conversation → extraction → confirm → Jira record (no duplicate on re-confirm)

## 7. Console commands

- [ ] 7.1 Provide a `summarize <key|channel> [--window]` handler that runs extraction and prints candidates
- [ ] 7.2 Provide `candidates list <key>` and `candidates confirm/reject <id>` handlers
- [ ] 7.3 Route console confirmations through `jira-decision-writeback`, honoring `--dry-run`
