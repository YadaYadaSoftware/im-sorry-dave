## Context

The platform's capabilities are mostly event-driven (webhooks, background workers). Operators
and developers need an imperative way to drive and inspect them. A console application that
shares the same Core services as the web host provides this without duplicating logic.

## Goals / Non-Goals

**Goals:**
- A single console app that drives every capability through verb-grouped commands.
- Identical behavior to the API by reusing the same DI registrations and config.
- Scriptable: structured output, exit codes, and dry-run for mutating commands.

**Non-Goals:**
- Replacing the web host (webhook intake and background workers stay there).
- A long-running daemon — the console runs a command and exits.
- A TUI/interactive shell.

## Decisions

- **Reuse Core DI via the Generic Host.** The console builds a `HostApplicationBuilder`, calls
  the same `AddJiraSyncCore` (and per-capability registration as those land), then resolves
  services to execute a command. Guarantees no behavioral drift from the API. *Alternative:*
  re-implement service wiring in the CLI — rejected (drift, double maintenance).
- **`System.CommandLine` for parsing.** Gives grouped subcommands, typed options, help, and
  tab-completion for free. *Alternative:* hand-rolled args parsing — rejected (reinvents help,
  validation, completion).
- **Background workers are not started.** The console registers Core services but does not run
  the hosted background services; commands invoke the underlying runners directly (e.g. call
  `ReconciliationRunner`/`WriteBackSender` once) so a command does discrete, observable work.
- **Thin shell; commands live with their capability.** Each capability change defines the
  commands that drive it (as spec requirements) and supplies the handler; the console framework
  only provides hosting, dispatch, output, and dry-run. Keeps ownership with the capability.
- **Dry-run is a global option enforced at the service-call boundary** so every mutating
  command honors it uniformly.

## Risks / Trade-offs

- [CLI and API behavior drift] → Mitigated by sharing the exact same service registrations.
- [Running a command starts background timers and causes surprises] → Console does not register
  hosted services; it invokes runners on demand.
- [Destructive command run by mistake] → Global `--dry-run` and explicit "performed" reporting;
  capability commands may additionally require confirmation.
- [Secrets exposure in a shared shell] → Console reads from the same secret providers; no
  secrets are echoed in output, including `--json`.

## Migration Plan

1. Add the console project referencing Core; wire the host + `System.CommandLine` root.
2. Implement the `console-control` framework (help, output, exit codes, dry-run).
3. Add the `jira-sync-core` command groups first (sync/workitem/writeback) since that
   capability exists; add other groups as their capabilities are implemented.
- *Rollback:* the console is additive; removing the project affects nothing else.

## Open Questions

- Should the console support a config/profile switch to target multiple environments?
- Output format default — table vs. plain text (with `--json` always available)?
