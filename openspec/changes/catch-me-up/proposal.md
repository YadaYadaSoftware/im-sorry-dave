## Why

When someone steps away from a busy work item and comes back, they have to scroll the Slack channel,
re-read Jira, and reconstruct what moved. The shared `PostCursor` marks what the platform last
`/post`ed — it is not personal, so it can't answer "what changed since *I* last looked." The platform
already mirrors the `WorkItem`, captures the conversation (`CapturedMessage` transcript), records
confirmed decisions (`WriteBackRecord`), and can summarize via Claude. Those seams are enough to give
each user a personal, on-demand "catch me up" digest.

## What Changes

- **`/catchup` slash command.** A user runs `/catchup` in a work-item channel (or DM) and gets an
  **ephemeral** digest, visible only to them, of what changed on that work item since **their own**
  last read.
- **Per-user read cursor.** Introduce a per-`(user, channel/work-item)` read cursor — distinct from
  the shared `PostCursor` — that marks how far that user has been caught up. The cursor advances after
  a successful digest.
- **Delta digest from existing seams.** The digest covers, since the user's cursor: `WorkItem`
  status/assignee changes, new Jira comments, confirmed decisions/answers (`WriteBackRecord`), and
  notable conversation (`CapturedMessage`). Claude (`IDecisionExtractor`/summarizer, gated on
  `Anthropic:ApiKey`) summarizes the delta; without a key it falls back to a plain grouped listing.
- **"Nothing new" case.** When nothing changed since the user's cursor, reply ephemerally that they
  are already caught up and leave the cursor unchanged.

## Capabilities

### New Capabilities
- `catch-me-up`: a per-user, on-demand `/catchup` digest of what changed on a work item since that
  user's personal read cursor, summarized via Claude with a graceful no-key fallback, replied
  ephemerally, then advancing the user's cursor.

## Impact

- `src/SorryDave.JiraSync.Core/Slack` — add a `/catchup` handler on the signature-verified slash
  endpoint (`/slack/commands`); resolve channel→work-item via `IMappingStore`; reply ephemerally.
- `src/SorryDave.JiraSync.Core` — add a per-`(user, work-item)` read-cursor store (new persistence,
  parallel to `PostCursor`); no change to `PostCursor` semantics.
- `src/SorryDave.JiraSync.Core` — read `WorkItem`, `WriteBackRecord`, and `CapturedMessage` since the
  user's cursor; assemble the delta.
- `IDecisionExtractor`/summarizer — summarize the assembled delta; fake fallback when `Anthropic:ApiKey`
  is absent. Secrets via SSM; AWS ECS. No change to Jira write-back.
