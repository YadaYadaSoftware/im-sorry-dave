# Tasks

## 1. Configuration model & target definitions

- [ ] 1.1 Add an `ApiTarget` model (`BaseUrl`, optional `WebhookSecret`) and bind `ApiTargets` as a `Dictionary<string, ApiTarget>` plus an `ActiveApiTarget` string
- [ ] 1.2 Define `local` (`http://localhost:5050`) and `aws` (`https://jsg.appcloud.systems`) targets in `appsettings.json`, with `ActiveApiTarget` defaulting to `local`
- [ ] 1.3 Ensure the TUI project has a `UserSecretsId` so `ApiTargets:aws:WebhookSecret` can be set out of the repo

## 2. Resolve targets & active selection (`AppServices`)

- [ ] 2.1 Rework `AppServices.Build` to return the resolved target set and the active target (name + `ApiTarget`)
- [ ] 2.2 Add the Aspire-injected endpoint (`services:api:http:0`/`https:0`) as an implicit `aspire` target, used by default unless `ActiveApiTarget` is set explicitly
- [ ] 2.3 Map a `--target <name>` command-line switch to `ActiveApiTarget`; fall back to the localhost default target when no targets are configured

## 3. Per-target webhook authentication (`ApiClient`)

- [ ] 3.1 Construct `ApiClient` with the active target's optional webhook secret
- [ ] 3.2 Append `?secret=<value>` to `POST /webhooks/jira` when a secret is set; send nothing extra when it is not
- [ ] 3.3 Never log or surface the secret in results, status bar, or error text

## 4. Runtime selection & visibility (`MainWindow`)

- [ ] 4.1 Show `Target: <name> (<url>)` for the active target in the status bar
- [ ] 4.2 Add a "Target" menu listing configured targets; selecting one rebuilds the `ApiClient` against the new base URL + secret and refreshes the active panel
- [ ] 4.3 Keep connection failures in-UI (point at an unreachable target without crashing)

## 5. Tests

- [ ] 5.1 Unit-test target resolution: default selection, `--target` override, Aspire implicit target, no-targets fallback
- [ ] 5.2 Unit-test that the webhook call includes the secret when configured and omits it when not (via an `HttpClient` test handler)

## 6. Configure & verify

- [ ] 6.1 Set the AWS webhook secret in the TUI user-secrets (`ApiTargets:aws:WebhookSecret`)
- [ ] 6.2 Verify `local`: list work items and simulate a webhook against the local API
- [ ] 6.3 Verify `aws`: switch target at runtime, list MDP work items, and simulate a webhook (the secured endpoint accepts it)

## 7. Docs

- [ ] 7.1 Document the multi-target configuration, `--target` / runtime switching, and the AWS webhook-secret user-secret in the README's smoke-TUI section
