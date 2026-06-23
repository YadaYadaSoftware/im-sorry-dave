# Tasks

## 1. Read-only assistant over the Claude seam

- [ ] 1.1 Add a read-only "answer" operation behind the existing Claude client pattern (the
      `IDecisionExtractor` Anthropic client, gated on `Anthropic:ApiKey`, deterministic fake when
      unset) taking `(WorkItem, bounded transcript window, question)` and returning answer text
- [ ] 1.2 Build the grounding context: load the mirrored `WorkItem` (key, summary, status, assignee,
      description) and a bounded transcript window from `CapturedMessage` (reuse the summarization
      window logic; truncate defensively to cap token cost)
- [ ] 1.3 Keep it strictly read-only — no Jira write, no decision write-back invocation

## 2. Entry points in Slack

- [ ] 2.1 Wire `/ask <key> <question>` into the signature-verified `/slack/commands` handler; parse the
      key, run the assistant, reply in the channel
- [ ] 2.2 Wire the bot @mention event path; resolve the work item from the channel via `IMappingStore`;
      reply in-thread
- [ ] 2.3 Acknowledge within Slack's slash timeout and post the answer when ready (async/deferred)

## 3. Graceful degradation

- [ ] 3.1 Unknown key / channel with no linked work item → reply "don't know that item" (or "not
      linked"); skip the Claude call
- [ ] 3.2 No `Anthropic:ApiKey` → reply "assistant unavailable"; any Claude failure → friendly
      fallback, never an unhandled error to Slack

## 4. Tests

- [ ] 4.1 `/ask <key> <question>` for a known item → grounded answer posted, no Jira write
- [ ] 4.2 Bot mention in a linked channel → work item resolved from channel, answer in-thread
- [ ] 4.3 Unknown/unlinked item → graceful reply, no Claude call
- [ ] 4.4 No Anthropic key → "assistant unavailable"; transcript window is bounded

## 5. Deploy & verify

- [ ] 5.1 Deploy; run `/ask <KEY> what did we decide?` and @mention the bot in a work-item channel →
      confirm grounded, read-only replies
- [ ] 5.2 Docs: note the read-only assistant (`/ask` + mention) in the README Slack section
