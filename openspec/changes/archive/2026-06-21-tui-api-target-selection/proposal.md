## Why

The smoke-test TUI currently resolves a **single** API base address from configuration, so pointing
it at a different backend means changing config and relaunching. Now that the platform runs in two
places at once — the **local debugging API** (`http://localhost:5050`, usually fake Jira) and the
**deployed AWS API** (`https://jsg.appcloud.systems`, real MDP) — we want the TUI to carry the
connection details for both and let the operator pick which one to drive, without editing config or
restarting.

## What Changes

- Configure the TUI with a **named set of API targets**, each carrying everything needed to talk to
  that API: a base URL and (for secured backends) the **webhook shared secret**. Local and AWS ship
  as the two out-of-the-box targets.
- Let the operator **choose the active target** — a default/CLI selection at startup and a **switch
  at runtime** from within the TUI — with the active target's name and URL shown in the status bar.
- Apply the selected target's **webhook secret** when calling the secured `POST /webhooks/jira`
  endpoint, so "Simulate webhook" works against the AWS API (which rejects unsigned requests) as
  well as local.
- Keep **Aspire service discovery** working: when the AppHost launches the console, the injected API
  endpoint is still honored (as an implicit/auto target).
- Sensitive per-target values (the webhook secret) come from **user-secrets / environment**, never
  the repo.

## Capabilities

### New Capabilities
- `tui-api-target-selection`: configuring multiple named API targets in the TUI, selecting the
  active one at startup and at runtime, and supplying each target's connection + auth details
  (base URL, webhook secret) so either backend can be driven.

### Modified Capabilities
- `tui-smoke-test`: the "Connected API is visible" requirement changes from a single
  config-resolved API to the **currently selected target** — the status bar shows the active
  target's name and address, and the connection follows the selection.

## Impact

- `src/SorryDave.JiraSync.SmokeTui` — `AppServices` (resolve a target list + active selection),
  `Api/ApiClient` (apply the webhook secret to the secured webhook call), `Ui/MainWindow` (status
  bar shows the active target; menu action to switch target), plus an `ApiTarget` config model and
  `appsettings.json` / user-secrets for target definitions.
- No server-side changes; the deployed API and its webhook secret are unchanged.
- README: document the TUI's multi-target configuration and how to switch.
