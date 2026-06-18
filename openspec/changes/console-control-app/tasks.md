## 1. Console project & host

- [ ] 1.1 Add the `SorryDave.JiraSync.Cli` console project to the solution, referencing Core
- [ ] 1.2 Build a Generic Host that calls the shared `AddJiraSyncCore` (no hosted background services)
- [ ] 1.3 Add `System.CommandLine` and define the root command with capability groups

## 2. Framework conventions

- [ ] 2.1 Implement discoverable help at root and per-group level
- [ ] 2.2 Implement the global `--json` output option and a consistent result formatter
- [ ] 2.3 Implement exit-code conventions (0 success, non-zero on failure with stderr message)
- [ ] 2.4 Implement the global `--dry-run` option enforced at the service-call boundary

## 3. Wire jira-sync-core commands

- [ ] 3.1 Implement the `sync` and `workitem` command handlers (backfill, reconcile, list, show)
- [ ] 3.2 Implement the `writeback` command handlers (submit, list, retry)
- [ ] 3.3 Verify real vs fake backend selection matches the API

## 4. Validation

- [ ] 4.1 Unit/integration tests for parsing, exit codes, `--json`, and `--dry-run` behavior
- [ ] 4.2 Document the command catalog in the README
