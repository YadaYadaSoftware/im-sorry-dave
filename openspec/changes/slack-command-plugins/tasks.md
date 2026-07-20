## 1. Contract and configuration

- [x] 1.1 Define the slash command plugin contract in `Core/Slack/`: command name, description,
      acknowledgement text, handler, and the interactivity actions the plugin owns. Follow the doc-comment
      style of `IWorkItemChangeListener` and `IJiraSlackIdentityResolver`, stating the ownership and
      registration rules on the interface itself.
- [x] 1.2 Define the context types passed to handlers (command invocation context and action context),
      carrying the fields the existing endpoint extracts: channel id, response URL, invoking user, and
      the raw form/payload values a handler needs.
- [x] 1.3 Define the handler result type used to deliver a message back through the response URL.
- [x] 1.4 Add `EnabledCommands` (string array) to `SlackOptions`, matching the style of the existing
      `EligibleIssueTypes` / `ClosedStatuses` settings, with a doc comment stating that absence means
      disabled.

## 2. Registry and dispatch

- [x] 2.1 Implement the registry: resolve a command by name, resolve an action's owning plugin by
      namespaced action id, and expose the registered set for manifest generation.
- [x] 2.2 Filter registration by the `EnabledCommands` allow-list, so a plugin whose name is absent is
      not registered. Verify an empty or unset allow-list registers nothing.
- [x] 2.3 Log at startup which plugins were discovered, which were registered, and which were skipped
      for not appearing in the allow-list, so a missing command is diagnosable.
- [x] 2.4 Register plugins and the registry in `AddJiraSyncCore` in `ServiceCollectionExtensions.cs`.
- [x] 2.5 Implement host-owned acknowledgement and background execution: acknowledge with the plugin's
      text inside Slack's deadline, then run the handler in its own DI scope outside the request
      lifetime, and deliver the result to the response URL. Preserve the reasoning comments from
      `SlackEventEndpoints.cs` about why the request scope and request cancellation token cannot be
      used, so the next reader does not undo it.
- [x] 2.6 Catch handler failures in the host and report a failure message to the invoking user, logging
      the exception.

## 3. Endpoint rewiring

- [x] 3.1 Rewrite the `/slack/commands` handler to read the `command` field from the form payload and
      dispatch through the registry, keeping signature verification ahead of dispatch and reusing the
      existing `ParseForm` and response URL helpers.
- [x] 3.2 Reply with an ephemeral "command is not available" message when the named command is not
      registered, invoking no handler.
- [x] 3.3 Rewrite the `/slack/interactivity` handler to resolve the owning plugin from the namespaced
      action id, replacing the hardcoded confirm/reject ternary.
- [x] 3.4 Refuse actions whose owning plugin is not registered, and actions with an unrecognized id,
      reporting that the action is no longer available rather than invoking a handler.

## 4. Relocate `/post`

- [x] 4.1 Move the `/post` body from the endpoint lambda into a plugin type, as a pure relocation — no
      rewriting, gating, or stubbing of the summarization logic, and no changes to
      `IConversationSummarizer`, `CandidateBlocks`, or the summarization internals.
- [x] 4.2 Move the Confirm and Reject handlers onto the same plugin as its owned actions.
- [x] 4.3 Change the action ids emitted by `CandidateBlocks` to the namespaced form, and update the
      handlers to match.
- [x] 4.4 Verify behavioral parity with `post` present in the allow-list: same outcomes, same messages,
      same write-back side effects as before the move.

## 5. Manifest generation

- [x] 5.1 Generate the manifest's `slash_commands` block from the registered command set, using each
      plugin's declared name and description.
- [x] 5.2 Regenerate `docs/slack-app-manifest.yaml`, preserving its existing explanatory header
      comments, scopes, event subscriptions, and interactivity settings.
- [x] 5.3 Add a test asserting the committed manifest matches what the registry would generate, so the
      two cannot drift.
- [x] 5.4 Confirm the `commands` OAuth scope is unchanged by the command list, so updating commands is a
      manifest re-upload rather than an app reinstall, and note this in the manifest header comments.

## 6. Tests

- [x] 6.1 Registry tests: command resolution by name, action resolution by namespaced id, and two
      plugins declaring the same bare action name resolving to different plugins.
- [x] 6.2 Allow-list tests: a listed command registers, an unlisted command does not despite its plugin
      existing, an empty or unset allow-list registers nothing, and re-adding a name re-enables the
      command.
- [x] 6.3 Dispatch tests: a signature-verified request routes to the owning plugin and no other, an
      unregistered command name gets the unavailable reply with no handler invoked, and a request
      failing signature verification is rejected before any plugin is resolved.
- [x] 6.4 Interactivity tests: an action routes to its owning plugin, an action for an unregistered
      plugin is refused, and an unrecognized action id invokes nothing.
- [x] 6.5 Acknowledgement tests: invocation is acknowledged with the plugin's text before the handler
      completes, a handler slower than the acknowledgement deadline still delivers via the response URL,
      a handler retains its services after the request has been acknowledged, and a throwing handler
      produces a reported failure rather than an unhandled error.
- [x] 6.6 Manifest tests: generated output lists exactly the registered commands, a disabled command is
      absent, and drift against the committed file fails.
- [x] 6.7 Confirm ≥80% line coverage on the new and changed code before pushing, per the project's
      coverage gate. This path has no existing coverage, so treat it as new code throughout.

## 7. Ship disabled

- [ ] 7.1 Ship with `post` absent from `Slack:EnabledCommands` across the deployed configuration,
      including the SSM parameter values that override `appsettings.json`.
- [ ] 7.2 Re-upload the regenerated manifest to Slack so `/post` disappears from autocomplete. Do this
      before or alongside the config change, since config alone only makes the command fail when typed.
- [ ] 7.3 Verify in the workspace that `/post` no longer appears in Slack's command autocomplete, and
      that a stale Confirm button on an existing candidate card is refused rather than writing back.
- [x] 7.4 Document the reversal procedure — add `"post"` to the allow-list, redeploy, regenerate and
      re-upload the manifest — in `INSTRUCTIONS.md` or the manifest header, since the Jira decision may
      be reversed.

## 8. Follow-up notes for other changes

- [x] 8.1 Note in `slack-jira-actions`, `writeback-approval-gates`, and `work-item-assistant` that they
      should be built against the plugin contract rather than the endpoint.
- [x] 8.2 Correct the stale claim in `openspec/changes/slack-jira-actions/design.md` that `IJiraClient`
      already has assignee and status mutations — it has exactly two write methods, `AddCommentAsync`
      and `UpdateCommentAsync` — and adjust that change's task list to account for the missing methods.
