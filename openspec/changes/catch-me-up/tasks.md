# Tasks

## 1. Per-user read cursor

- [ ] 1.1 Add a per-`(user, channel/work-item)` read-cursor store (persistence parallel to `PostCursor`,
      keyed by Slack user id + work item resolved via `IMappingStore`); do not change `PostCursor`
- [ ] 1.2 Define a baseline default for first-time users (no cursor yet), e.g. work-item/channel creation
      point, so the first `/catchup` is bounded
- [ ] 1.3 Read and advance helpers (load cursor, compute high-water mark, persist on delivery)

## 2. `/catchup` command and delta assembly

- [ ] 2.1 Add the `/catchup` handler on the signature-verified slash endpoint (`/slack/commands`);
      resolve channel→work-item via `IMappingStore`; ephemeral "no work item linked here" for DM/unmapped
- [ ] 2.2 Assemble the delta since the user's cursor from `WorkItem` (status/assignee changes), new Jira
      comments, `WriteBackRecord` (confirmed decisions), and `CapturedMessage` (notable conversation)
- [ ] 2.3 Cap/prioritize the `CapturedMessage` slice (decision/mention-bearing first, then recency)

## 3. Summarize and respond

- [ ] 3.1 Summarize the delta via Claude (`IDecisionExtractor`/summarizer) when `Anthropic:ApiKey` is set
- [ ] 3.2 Deterministic grounded fallback listing when `Anthropic:ApiKey` is absent (fake fallback)
- [ ] 3.3 Reply ephemerally to the invoking user only; on "nothing new," say so and do not advance the cursor
- [ ] 3.4 Advance the user's read cursor only after a successful digest delivery

## 4. Tests

- [ ] 4.1 Delta digest: status/assignee + new comments + decisions + notable messages summarized and
      replied ephemerally; cursor advances to the high-water mark
- [ ] 4.2 No-key fallback returns the grounded listing of the same delta
- [ ] 4.3 Nothing-new returns the caught-up reply and leaves the cursor unchanged
- [ ] 4.4 DM/unmapped channel returns the "no work item linked" reply with no cursor action
- [ ] 4.5 First-time user (no cursor) uses the baseline and then advances; one user's catch-up does not
      affect `PostCursor` or other users' cursors

## 5. Deploy & verify

- [ ] 5.1 Deploy (SSM secrets, ECS); run `/catchup` after activity → confirm a private ephemeral digest
      and an advanced cursor; run again immediately → "nothing new"
- [ ] 5.2 Docs: note the `/catchup` command and the per-user read cursor in the README Slack section
