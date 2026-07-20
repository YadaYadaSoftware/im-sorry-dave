## Why

Our Jira administrator has told us the app will not be permitted to post comments or write to Jira in
any way. That decision may or may not be permanent. Today `/post` — whose entire purpose is producing
Jira write-back candidates — is hardwired into the Slack commands endpoint, so the only ways to comply
are to delete the feature or to leave a command in Slack that users can run and that appears to modify
Jira. Neither is acceptable: we need the command to disappear from Slack while its implementation stays
intact, and we need turning it back on to be a one-line configuration change rather than a revert.

The same seam is needed regardless of the Jira decision. `/post` is the only slash command today, and
the commands endpoint never reads the `command` field at all — it assumes every inbound slash command
is `/post`. Roughly eight more commands (`/catchup`, `/linkme`, `/decisions`, `/ask`, `/assign`,
`/triage`, `/status`) are queued across existing proposals and would otherwise land as a growing
`switch` statement, each one re-implementing Slack's 3-second acknowledgement dance by hand.

## What Changes

- Introduce a common interface every Slack slash command implements. The unit is a **feature**, not a
  bare command: a plugin owns its slash command *and* the interactivity actions that command produces.
  `/post` therefore owns its Confirm/Reject buttons, which are currently a hardcoded ternary on
  `action_id` in the endpoint.
- Add a registry and a real dispatcher. The commands endpoint reads `command` from the form payload and
  resolves it through the registry. Unknown or disabled commands get a polite ephemeral reply instead of
  silently running `/post`.
- Namespace interactivity action ids by plugin (`post:confirm` rather than bare `confirm`) so plugins
  cannot collide as more of them land.
- Move Slack's ack-then-background pattern into the host. Plugins supply acknowledgement text and a
  handler; the host owns the 3-second deadline, the background task, and the DI scope. This logic is
  currently hand-rolled in the endpoint — complete with a deliberate `CancellationToken.None` and a
  manually created service scope — and would otherwise be repeated by every future command.
- Add `Slack:EnabledCommands`, a string array on the existing `SlackOptions`, as the administrator's
  control point. It is an **allow-list: a command absent from the array is disabled.** This is
  fail-safe by default — a future write-capable command cannot ship silently enabled because someone
  forgot a flag.
- Move `/post`'s existing implementation into a plugin **without rewriting, gating, or stubbing it**,
  and ship with `post` absent from the allow-list. The code remains; only its registration changes.
- Generate `docs/slack-app-manifest.yaml` from the enabled command set, with a test asserting the
  committed file matches the registry. Configuration is the source of truth and the manifest is a
  generated artifact — the relationship is one-directional and the app never reads the manifest back.

Not breaking: no existing behavior changes when a command is enabled. `/post` enabled behaves exactly
as it does today.

## Capabilities

### New Capabilities

- `slack-command-registry`: The plugin contract every slash command implements, the allow-list
  configuration that determines which commands are registered, dispatch of inbound slash commands and
  interactivity actions to the owning plugin, host-owned acknowledgement and background execution, and
  generation of the Slack app manifest from the enabled set.

### Modified Capabilities

None. `/post`'s behavioral requirements are unchanged and live in the unarchived
`slack-conversation-summarization` change rather than in `openspec/specs/`, so there is no main spec to
delta against — see Impact for the coordination this implies.

## Impact

**Code**

- `src/SorryDave.JiraSync.Api/Endpoints/SlackEventEndpoints.cs` — the `/slack/commands` handler becomes
  a dispatcher; the `/slack/interactivity` handler routes by namespaced action id. Signature
  verification, form parsing, and the `response_url` helpers are reused unchanged.
- `src/SorryDave.JiraSync.Core/Slack/` — new interface, registry, and context types, following the
  existing `IWorkItemChangeListener` and `IJiraSlackIdentityResolver` plugin idioms already in the
  codebase.
- `/post`'s body relocates from the endpoint lambda into a plugin type. `IConversationSummarizer`,
  `CandidateBlocks`, and the summarization internals are untouched.
- `src/SorryDave.JiraSync.Core/Configuration/SlackOptions.cs` — add `EnabledCommands`, matching the
  existing `EligibleIssueTypes` / `ClosedStatuses` array-option style.
- `src/SorryDave.JiraSync.Core/DependencyInjection/ServiceCollectionExtensions.cs` — register plugins
  and the registry within the existing `AddJiraSyncCore`.
- `docs/slack-app-manifest.yaml` — becomes generated output rather than a hand-maintained file.

**Operational**

- Removing a command from the allow-list stops it being served; removing it from the manifest is what
  makes it disappear from Slack's autocomplete. Both are needed, and the manifest must be re-uploaded
  to Slack for the disappearance to take effect.
- Changing the command list does **not** change OAuth scopes — the `commands` scope covers all slash
  commands — so this is a manifest re-upload, not an app reinstall.

**Testing**

- There is currently **zero test coverage on the slash command path**. Project rules impose a hard
  ≥80% line coverage gate on new and changed code before push, so test work for the registry,
  dispatcher, allow-list filtering, and manifest generation is in scope rather than deferred.

**Out of scope** (deliberately, to keep this change small)

- `POST /workitems/{key}/writeback` and the SmokeTui client. This change concerns the Slack command
  surface only.
- Write-back outbox records, statuses, draining, and migration. Existing records are test data.
- Sweeping historical candidate cards already posted in Slack, which still render Confirm buttons in
  channel history.
- Any per-operation capability or permission vocabulary. The Jira restriction is blanket for now, and
  simple per-command on/off is sufficient.

**Follow-ups for other changes** (noted, not scoped here)

- The pending `slack-jira-actions`, `writeback-approval-gates`, and `work-item-assistant` proposals
  should be rebased onto this plugin interface rather than the current endpoint.
- `openspec/changes/slack-jira-actions/design.md` contains a stale claim that `IJiraClient` already has
  assignee and status mutations. It does not — the interface has exactly two write methods,
  `AddCommentAsync` and `UpdateCommentAsync`. That proposal's task list needs correcting.
- Under a blanket write ban the queued read-side commands (`/catchup`, `/decisions`, `/linkme`,
  `/status`, `/ask`) are unaffected and are where the roadmap can continue.
