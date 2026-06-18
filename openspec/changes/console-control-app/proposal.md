## Why

Operators and developers need a direct way to drive and inspect the platform's functions —
trigger a backfill, submit a write-back, provision a Slack channel, run summarization, link a
PR, generate Jira items — without going through Swagger, webhooks, or waiting for background
timers. A console application gives a scriptable, automatable surface over the same Core
services the web host uses, which is invaluable for testing, one-off operations, and local
review of capabilities that are otherwise event-driven.

## What Changes

- Add a .NET console application that hosts the platform's Core services (shared DI, config,
  and secrets) and dispatches verb-based commands grouped by capability.
- Provide a consistent command framework: discoverable help, structured (`--json`) output,
  meaningful exit codes, and a global `--dry-run` for commands that would mutate external systems.
- Each capability contributes its own command group (added to that capability's spec): the
  console is a thin shell that calls the same services the API uses, so behavior cannot drift
  between the two surfaces.

## Capabilities

### New Capabilities
- `console-control`: The console application's command framework — host/config sharing, command
  dispatch and help, output formatting, exit codes, and dry-run/confirmation conventions that
  every capability's commands follow.

### Modified Capabilities
<!-- The console *command set* for each capability is added within that capability's own change
     (as ADDED requirements), since the commands drive that capability's services:
     jira-work-item-sync, jira-decision-writeback, slack-channel-lifecycle, slack-jira-linkage,
     claude-decision-extraction, summary-writeback-trigger, github-pr-linkage,
     github-status-automation, openspec-spec-linkage, openspec-item-generation. -->

## Impact

- New console project (e.g. `SorryDave.JiraSync.Cli`) in the solution, referencing Core and,
  as they are built, each capability library.
- Reuses the existing configuration, secrets, and DI registration (`AddJiraSyncCore` and the
  equivalent for each capability) via the .NET Generic Host.
- No new external dependencies beyond a command-line parsing library.
- The console is an operator tool; it does not replace the web host's webhook intake or
  background workers.
