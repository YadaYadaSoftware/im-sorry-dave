## Context

The platform already mirrors Jira work items, provisions a Slack channel per item, captures the
channel conversation as `CapturedMessage` (thread-faithful, keyed by `(ChannelId, Ts)`), and runs
Claude over a transcript window through the `IDecisionExtractor` seam — the Anthropic Messages API
client gated on `Anthropic:ApiKey`, with a deterministic fake when the key is unset. Slack endpoints
live under `/slack` with signature-verified slash handling at `/slack/commands`, and `IMappingStore`
maps a Slack channel to its work item. This change adds a **read-only** question-answering surface on
top of that existing material: ask about a work item, get a grounded Claude answer in Slack. Nothing
here writes to Jira — that remains the `/post` write-back path.

## Goals / Non-Goals

**Goals:**
- Answer a natural-language question about a work item, grounded in the mirrored `WorkItem` fields and
  the captured transcript for that item.
- Two entry points: a `/ask <key> <question>` slash command and an @mention of the bot in a work
  item's channel; reply in the same channel/thread.
- Degrade gracefully: unknown/unlinked item → clear "don't know it"; no Anthropic key → "assistant
  unavailable" (not an error).
- Bound the transcript context sent to Claude to cap token cost.

**Non-Goals:**
- Writing to Jira or persisting anything (read-only; no `/post`, no decision write-back here).
- Cross-item or global search; the assistant is scoped to one work item per question.
- Multi-turn memory / conversational state beyond the single question and its grounding context.
- Changing channel provisioning, membership, or capture.

## Decisions

### Reuse the Claude seam; add a read-only "answer" capability behind it
**Decision:** Answer via the existing Claude client pattern (the `IDecisionExtractor` Anthropic
client, gated on `Anthropic:ApiKey`, fake when unset). Introduce a focused assistant operation —
either a new method on the seam or a sibling `IWorkItemAssistant` that wraps the same Anthropic client
— that takes `(WorkItem, bounded transcript window, question)` and returns answer text. *Why:* one
Claude integration, one gating/secret path (SSM `Anthropic:ApiKey`), one fake. The prompt differs
(answer a question vs. extract candidates), but the client, key gating, and fake behavior are shared.

### Two entry points, one assistant
**Decision:** `/ask <key> <question>` arrives at the signature-verified `/slack/commands` handler; the
key is parsed from the command text. The @mention path arrives as a Slack event in a channel; resolve
the work item from the channel via `IMappingStore` (no key needed in the message). Both funnel into
the same assistant call and reply in the channel (mention replies in-thread). *Why:* the grounding and
read-only contract are identical; only how we obtain the work item key differs.

### Strictly read-only and clearly separated from write-back
**Decision:** The assistant never calls Jira and never invokes the write-back path. It only reads the
`WorkItem` and `CapturedMessage` rows and posts a Slack reply. *Why:* "ask" is for understanding;
`/post` is for recording a decision into Jira. Keeping them separate avoids accidental writes from a
question and keeps the trust boundary obvious.

### Bound the context handed to Claude
**Decision:** Build the transcript window from a bounded slice of `CapturedMessage` for the item
(e.g. most recent N messages / a token budget, threads collapsed as for summarization) plus the
compact `WorkItem` fields (key, summary, status, assignee, description). Truncate defensively. *Why:*
caps token cost and latency; a work item channel can be long, and most questions are answerable from
the recent window plus the mirrored fields. *Alternative — whole transcript:* rejected (unbounded
cost). *Alternative — retrieval/embeddings:* out of scope here; revisit if recency proves insufficient.

### Graceful degradation
**Decision:** Unknown key or a channel with no linked work item → reply that the item isn't known /
the channel isn't linked (no Claude call). `Anthropic:ApiKey` unset → reply "assistant unavailable"
(the fake is for tests/determinism, not a user-facing answer). Any Claude failure → a friendly
fallback, never an unhandled error to Slack. *Why:* the surface must be safe and quiet when its inputs
or dependencies are missing.

## Risks / Trade-offs

- [Cost / runaway tokens on long channels] → bound the transcript window (N messages / token budget)
  and truncate; tune the bound.
- [Hallucinated answers] → ground strictly in the supplied `WorkItem` + transcript and prompt Claude
  to answer only from that context and say when it doesn't know; the assistant is advisory, read-only.
- [Mistaken Jira writes from "ask"] → the assistant has no write path at all; separation from `/post`
  is structural.
- [Slack 3-second slash timeout vs. Claude latency] → acknowledge immediately and post the answer when
  ready (async / deferred response), as the existing Slack handlers do.
- [No Anthropic key in an environment] → explicit "assistant unavailable" reply; no failure.

## Migration Plan

1. Add the read-only assistant operation behind the existing Claude seam (shared client + key gating +
   fake), taking `(WorkItem, bounded transcript window, question)`.
2. Add the work-item loader + bounded transcript window builder (reuse the summarization window logic).
3. Wire `/ask <key> <question>` into the signature-verified `/slack/commands` handler; reply in channel.
4. Wire the bot @mention event path; resolve the work item from the channel via `IMappingStore`; reply
   in-thread.
5. Handle unknown/unlinked item and missing key (graceful replies); tests for both entry points,
   read-only behavior, context bounding, and degradation.
6. Deploy; ask `/ask <KEY> what did we decide?` and @mention the bot in a channel → grounded replies.

## Open Questions

- Exact context bound (message count vs. token budget) — start with the summarization window size and
  tune from real channels.
- Whether `/ask` should also work outside a work-item channel (it can, since the key is explicit) — yes
  by default; the mention path is channel-scoped.


> **Build any slash command as a plugin.** Since `slack-command-plugins` landed, commands are not wired
> into `SlackEventEndpoints`: each implements `ISlackCommandPlugin`, owns its interactivity actions under
> namespaced action ids (`plugin:action`), and is served only when named in the `Slack:EnabledCommands`
> allow-list. The host owns Slack's ack-then-background handling, so handlers need not manage it.
> Commands that write to Jira cannot currently be enabled.
