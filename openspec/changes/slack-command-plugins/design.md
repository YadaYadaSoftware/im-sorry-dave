## Context

`POST /slack/commands` currently has no dispatch at all. It verifies the Slack signature, parses the
form, pulls `channel_id` and `response_url`, and then runs the `/post` summarization flow. The
`command` field of the payload is never read anywhere in the codebase. This works only because `/post`
is the sole command and the manifest points every command at the same URL.

Two pressures land on that design at once. Our Jira administrator has banned Jira writes, possibly
temporarily, so `/post` must stop being offered in Slack without its implementation being deleted. And
roughly eight further commands are queued across existing proposals, each of which would otherwise
extend a `switch` and re-implement the same acknowledgement plumbing.

That plumbing is the non-obvious part. Slack requires acknowledgement within about three seconds, but
`/post` calls Claude and routinely exceeds it. The endpoint therefore acknowledges immediately and
continues in a fire-and-forget `Task.Run`, which requires manually creating a DI scope, because the
request scope is disposed once the response is written, and deliberately passing `CancellationToken.None`,
because the request's cancellation token is cancelled at the same moment. Both are easy to get wrong
and neither is obvious from the outside. Repeating it per command is the failure mode to design out.

The codebase already has three plugin-shaped seams to imitate rather than invent: `IWorkItemChangeListener`
(fan-out, best-effort, unregistered when unconfigured), `IJiraSlackIdentityResolver` (an ordered chain
of strategies), and config-swapped real/fake backends in `AddJiraSyncCore`. Options types bind to
configuration sections and already carry array-valued settings such as `EligibleIssueTypes`, so an
allow-list fits the existing conventions.

## Goals / Non-Goals

**Goals:**

- A single contract every slash command implements, with a registry and a real dispatcher.
- An administrator-facing on/off control that requires no code change in either direction, because the
  Jira decision may be reversed.
- `/post`'s implementation preserved intact and merely unregistered.
- Acknowledgement, background execution, and scope management owned once by the host.
- A generated Slack manifest that cannot drift from the registry.
- Test coverage on a path that currently has none, sufficient for the project's ≥80% gate on new code.

**Non-Goals:**

- Any capability or permission vocabulary. The Jira restriction is blanket; per-command on/off is
  sufficient and a capability model would be speculative generality.
- Changing what `/post` does. This change relocates it; it does not touch summarization.
- Write-back outbox behavior, statuses, draining, or migration. Existing records are test data.
- `POST /workitems/{key}/writeback` and the SmokeTui client, which also reach Jira but are not part of
  the Slack command surface.
- Sweeping historical candidate cards in Slack channel history.
- Dynamic or runtime plugin loading. Plugins are compiled in; only registration is configurable.

## Decisions

### The plugin unit is a feature, not a bare command

A plugin owns its slash command **and** the interactivity actions that command produces. `/post` posts
candidate cards carrying Confirm and Reject buttons, currently dispatched by a hardcoded ternary on
`action_id` in the interactivity endpoint.

*Alternative considered:* plug commands only, leaving interactivity as-is. Rejected because it produces
a half-seam. Disabling `/post` would leave its Confirm handler — the thing that actually submits to
Jira write-back — live and orphaned, reachable from any candidate card still sitting in channel
history. Coupling actions to their command makes "disabled" mean disabled.

### Action ids are namespaced by plugin

Action ids become `<plugin>:<action>` — `post:confirm` rather than bare `confirm`. With one command
this is unnecessary; with eight it prevents two plugins from claiming the same identifier, and it lets
the dispatcher resolve the owning plugin from the id alone without a central action table.

Cards posted before this change carry un-namespaced ids. Since a bare id matches no registered plugin,
those clicks fall through to the unrecognized-action path and are refused. That is the desired outcome
here, and it is why the spec calls out the stale-button scenario explicitly.

### The allow-list is opt-in, with absence meaning disabled

`Slack:EnabledCommands` is a string array on the existing `SlackOptions`. A command is registered only
if named there.

*Alternative considered:* per-command `Enabled: false` flags, or a deny-list. Both rejected because
they fail open. A future write-capable command would ship enabled unless someone remembered to disable
it — precisely the failure this change exists to prevent. An allow-list also gives the administrator a
single line to read to know the app's entire command surface.

The cost is real and accepted: a newly built command is invisible until someone lists it, which will
briefly confuse developers. Registration should log which plugins were found and which were skipped, so
the reason is discoverable rather than mysterious.

### The host owns acknowledgement and background execution

Plugins declare acknowledgement text and a handler. The host acknowledges within the deadline, creates
the execution scope, runs the handler outside the request lifetime, and delivers the result to the
response URL, catching failures and reporting them to the user.

*Alternative considered:* let each plugin manage its own acknowledgement, since some commands are fast
enough to answer inline. Rejected: two ways to answer a command means every future plugin author makes
a decision they do not have context for, and the fast path is an optimization for a problem we do not
have. Uniform acknowledgement also means the `CancellationToken.None` and manual-scope subtleties are
written once, in one place, with the reasoning attached.

### The manifest is generated from the registry, one direction only

Configuration determines the registry; the registry generates the manifest. The app never reads the
manifest back.

*Alternative considered:* commit the manifest and have the app parse it at startup to decide which
commands to serve, giving a single source of truth. Rejected for two reasons. The manifest is also
editable in Slack's own UI, so it is not reliably ours. And it is a Slack-side artifact with no
vocabulary for anything beyond Slack — treating it as application configuration would push us to encode
non-Slack concerns in it later.

This split means the two layers do different jobs, and both are needed. Configuration makes a command
*fail* if typed; the manifest makes it *disappear* from autocomplete. Neither alone satisfies the
requirement that the command not appear to exist.

A test asserts the committed manifest matches what the registry would generate, which also fixes an
existing problem: the manifest is hand-maintained today with nothing checking it.

### Command list changes do not require app reinstall

The `commands` OAuth scope covers all slash commands, so changing which commands are declared does not
change the requested scopes. Updating the command list is a manifest re-upload, not a reinstall or
re-authorization. This keeps the reversal cost low.

## Risks / Trade-offs

**The manifest re-upload is a manual step outside the deployment** → Flipping the allow-list stops the
command being served immediately, but it remains in Slack's autocomplete until someone re-uploads the
manifest, leaving a window where users can type a command that now refuses them. Mitigated by ordering
the runbook manifest-first when disabling, and by the generation test keeping the file honest. See Open
Questions on automating this away.

**Allow-list fails closed, including by accident** → A typo or a missed entry silently removes a
command from production. Mitigated by logging discovered-but-skipped plugins at startup and by the
manifest test, which surfaces the mismatch as a build failure rather than a support ticket.

**Relocating `/post` risks changing it** → The move touches working code that has no test coverage on
the command path. Mitigated by writing the dispatcher and registry tests first, and by treating the
relocation as a pure move: no rewriting, no gating, no stubbing. Behavior preservation is stated as a
requirement in the spec precisely so it is verifiable rather than assumed.

**Stale Confirm buttons remain visible in channel history** → Config cannot recall messages already
posted. Clicks are refused safely, but the button still renders. Accepted for now: the affected data is
test data. If the restriction is enforced strictly against appearance rather than effect, a card sweep
becomes a follow-up change, and it would require the candidate cards' Slack message timestamps to have
been persisted — worth confirming before promising it.

**Uniform background execution adds latency to trivial commands** → Even a command that could answer
instantly gets an acknowledgement followed by a response-URL delivery. Accepted: the consistency is
worth more than the milliseconds, and Slack renders both smoothly.

## Migration Plan

1. Land the contract, registry, dispatcher, and host-owned acknowledgement with tests, while the
   endpoint still behaves as it does today.
2. Relocate `/post` into a plugin, including its Confirm/Reject actions under namespaced ids. Verify
   parity with `post` present in the allow-list.
3. Add manifest generation and the drift test; regenerate the committed manifest.
4. Ship with `post` **absent** from the allow-list, and re-upload the generated manifest to Slack so
   the command disappears from autocomplete.

**Rollback:** revert to the previous deployment; the endpoint's behavior is unchanged for enabled
commands, so nothing downstream depends on the new shape.

**Reversing the Jira decision:** add `"post"` to `Slack:EnabledCommands`, redeploy, regenerate and
re-upload the manifest. No code change.

## Open Questions

- **Should manifest upload be automated?** Slack exposes an app-manifest update API driven by a
  rotating app-configuration token. Automating it would make both disabling and re-enabling a single
  configuration change rather than a two-step runbook, which matters given the whole point of this
  change is cheap reversal. Deferred for now to keep scope small; the current auth flow and token
  rotation requirements should be verified before designing against it. Revisit if the Jira decision
  reverses, or if manifest drift shows up in practice.
- **Should the registry expose an operator read-out** of which commands are registered and which were
  skipped, alongside the existing admin endpoints? Startup logging covers the immediate need, but an
  endpoint would let an administrator confirm the live surface without shell access.
