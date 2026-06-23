## Why

People in a work item's Slack channel constantly ask questions whose answers already live in the
data the platform holds — the mirrored `WorkItem` (status, assignee, description) and the captured
Slack transcript (`CapturedMessage`). Today they have to scroll the channel or open Jira and read.
The platform already runs Claude over the transcript for write-back summarization; it can just as
easily answer a question **read-only**, grounded in the same material, without ever touching Jira.

## What Changes

- **Conversational assistant over a work item.** A user can ask a question about a work item and get
  a Claude answer grounded in the mirrored `WorkItem` fields plus the captured Slack transcript for
  that item — e.g. "what did we decide?", "what's blocking this?", "summarize the current state."
- **Two entry points.** A `/ask <key> <question>` slash command (signature-verified, under `/slack`),
  and @mentioning the bot in a work item's channel (the work item is resolved from the channel via
  `IMappingStore`). The reply is posted in the same channel/thread.
- **Strictly read-only.** The assistant only reads the `WorkItem` + transcript and answers; it never
  writes to Jira. This is distinct from the `/post` decision write-back path.
- **Graceful degradation + cost bounding.** Unknown or unlinked work item → a clear "I don't know
  that item" reply. No Anthropic key → "assistant unavailable" (no error). The transcript context
  handed to Claude is bounded (recent/most-relevant window) to cap token cost.

## Capabilities

### New Capabilities
- `work-item-assistant`: answer a user's natural-language question about a work item in Slack,
  grounded read-only in the mirrored `WorkItem` and the captured transcript, via a slash command or
  an @mention, replying in the channel/thread; degrade gracefully and bound the Claude context.

## Impact

- `src/SorryDave.JiraSync.Api/Endpoints/SlackEndpoints.cs` — add the signature-verified
  `/slack/commands` slash handler for `/ask <key> <question>` and the bot-mention event path.
- `src/SorryDave.JiraSync.Core` — a new read-only assistant service that loads the `WorkItem` and the
  bounded transcript window and calls the Claude seam; resolves the work item from the channel via
  `IMappingStore` for the mention path.
- Reuses the Claude client pattern behind `IDecisionExtractor` (Anthropic Messages API, gated on
  `Anthropic:ApiKey`, deterministic fake when unset) — answering is a new prompt, not write-back.
- No Jira writes, no schema change; reads `WorkItem` + `CapturedMessage` only.
