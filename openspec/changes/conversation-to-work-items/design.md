## Context

The platform already captures Slack conversation (`CapturedMessage`), summarizes via Claude
(`IDecisionExtractor`, gated on `Anthropic:ApiKey` with a fake fallback), posts interactive Block Kit
candidate cards confirmed/rejected at `/slack/interactivity`, and writes confirmed records to Jira
through an idempotent outbox (`IWriteBackService`). Jira is the source of truth. The one missing seam
is creation: `IJiraClient` can get/search and add/edit **comments**, but it cannot **create issues**.
This change reuses the existing extract → card → confirm → write-back pipeline and adds the
create-issue capability so a conversation becomes new Jira issues.

## Goals / Non-Goals

**Goals:**
- A human-triggered command turns a channel's recent conversation (or pasted notes) into candidate
  action items (title, description, issue type).
- Each candidate is confirmed/rejected via the existing Block Kit pattern.
- On confirm, the item is **created in Jira** as a new issue, linked back to the originating work item
  / channel where one exists, attributed to the confirming user, and created **idempotently**.

**Non-Goals:**
- Auto-creating issues without human confirmation.
- Two-way sync / updates of created issues (creation only; existing sync handles the rest).
- Replacing the existing summarization candidate flow (this extends the same pattern to creation).
- Choosing assignees / sprints / estimates beyond title, description, and issue type.

## Decisions

### Trigger via a channel slash command
**Decision:** Add a slash command (`/triage`, alias `/actions`) handled alongside the existing Slack
command handling. With no argument it extracts over the channel's recent `CapturedMessage` history;
with pasted text it extracts over that text. The command resolves the channel's linked work item via
`IMappingStore` (if any) to drive back-linking. *Why a command:* explicit, human-initiated, and reuses
the existing Slack ingress; no new always-on listener.

### Reuse `IDecisionExtractor` to produce candidates
**Decision:** Extend / reuse `IDecisionExtractor` to return candidate **action items**, each with
`Title`, `Description`, and `IssueType`. It stays gated on `Anthropic:ApiKey` with the fake fallback
so tests and key-less environments degrade deterministically. *Why reuse:* the summarization path
already proves the gated-Claude seam; action-item extraction is the same shape with a create-oriented
output.

### Confirm with the existing Block Kit card pattern
**Decision:** Render one interactive card per candidate (title, description, issue type) with
Confirm / Reject buttons, routed through `/slack/interactivity` exactly like today's candidate cards.
The button `value`/`action_id` carries the candidate payload plus the dedupe key (below). *Why:* no new
UI surface; the confirm/reject contract already exists.

### New create-issue capability on `IJiraClient`
**Decision:** Add `CreateIssueAsync(projectKey, issueType, summary, description, …)` returning the new
issue key. The created issue's **project** and **issue type** come from config (a default project key
plus an issue-type mapping in SSM), overridable per candidate where the extractor proposes a type.
*Why noted explicitly:* `IJiraClient` does not create issues today — this is the one genuinely net-new
Jira capability this change introduces.

### Create through the idempotent outbox with a dedupe key
**Decision:** On confirm, enqueue the create via `IWriteBackService` / the outbox using a deterministic
**dedupe key** derived from the candidate (e.g. channel + source-message/notes hash + candidate
index/title). The outbox records the resulting Jira key against that dedupe key, so re-confirming the
same candidate finds the existing record and returns the already-created issue instead of creating a
second one. *Why a content-derived key:* re-clicking Confirm (Slack retries, double taps, re-runs of
`/triage`) must never double-create; the marker lives where the existing idempotency already lives.

### Back-link and attribute
**Decision:** When the channel maps to a work item (`IMappingStore`), link the new issue back to that
originating work item (and note the Slack channel as the source). Attribute the creation to the
**confirming Slack user** (recorded on the create + reflected in a Slack confirmation message naming
the new issue key). *Why:* preserves provenance — who turned the conversation into work, and what it
came from.

## Risks / Trade-offs

- [Double-create on re-confirm / Slack retries] → content-derived dedupe key recorded by the outbox;
  re-confirm returns the existing issue.
- [Wrong project / issue type] → config-driven default project + issue-type mapping (SSM), with the
  candidate's proposed type as an override; surfaced on the card before confirm.
- [Claude unavailable / no API key] → gated on `Anthropic:ApiKey` with the fake fallback; the command
  degrades deterministically rather than failing.
- [Noisy / low-quality candidates] → human confirm/reject per card is the gate; nothing is created
  without a confirm.
- [Pasted-text size] → bound/truncate the input passed to the extractor defensively.

## Migration Plan

1. Add `CreateIssueAsync` to `IJiraClient` + implementation; config for default project key and
   issue-type mapping (SSM).
2. Extend `IDecisionExtractor` to emit candidate action items (title, description, issue type),
   gated + fake fallback.
3. Add the `/triage` (`/actions`) command handler; extract over recent `CapturedMessage` or pasted
   text; resolve channel → work item via `IMappingStore`.
4. Render candidate cards (Block Kit) and route Confirm/Reject at `/slack/interactivity`; embed the
   dedupe key in the action payload.
5. On confirm, create via `IWriteBackService` / outbox with the dedupe key; back-link + attribute;
   post a confirmation message naming the new issue key.
6. Tests: extraction → cards; confirm → one issue created; re-confirm → no second issue; reject → no
   issue; channel with no mapping → created without back-link.
7. Deploy; run `/triage` in a channel, confirm a candidate, verify a new Jira issue exists and links
   back.

## Open Questions

- Default project key when the channel maps to no work item — fall back to a single configured default
  project, or refuse and ask? Start with a configured default; refuse if unset.
- Issue-type source of truth — trust the extractor's proposed type, or always map through config?
  Start config-mapped with the extractor's type as a hint.
