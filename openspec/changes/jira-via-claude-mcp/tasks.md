# Tasks

## 1. Backend selection

- [ ] 1.1 Add a `Jira:Backend` selector (`Fake | Rest | ClaudeMcp`) to `JiraOptions`, back-compatible
      with `UseFake` and the existing credentials-present default
- [ ] 1.2 In `ServiceCollectionExtensions`, register the backend by `Jira:Backend` (Fake/Rest
      unchanged; ClaudeMcp added)

## 2. Anthropic + MCP client

- [ ] 2.1 Add `AnthropicOptions` (API key, model, Atlassian MCP server URL + auth) sourced via the
      secrets convention (`/jira-sync/Anthropic/ApiKey`)
- [ ] 2.2 Implement an Anthropic Messages API client with the MCP connector configured for the
      Atlassian MCP server (constrained tool use, structured output)

## 3. `ClaudeMcpJiraClient : IJiraClient`

- [ ] 3.1 `GetIssueAsync` / `SearchAsync` → Atlassian MCP get-issue / search-by-JQL; map results into
      `JiraIssueData`; validate and log on malformed output
- [ ] 3.2 `AddCommentAsync` / `UpdateCommentAsync` → MCP add/edit comment, posting the exact body
      (idempotency marker preserved); `GetCommentAsync` → fetch comment ADF for mentions
- [ ] 3.3 Keep all calls best-effort/typed so `ReconciliationRunner` and `WriteBackSender` need no
      change

## 4. Webhook-only steady state

- [ ] 4.1 Default this backend to webhook-only sync; make backfill/reconcile (the Claude reads)
      occasional and disable-able via config

## 5. Tests

- [ ] 5.1 Backend selection (Fake/Rest/ClaudeMcp; default preserves behavior)
- [ ] 5.2 Stubbed Anthropic/MCP transport: get/search/add/edit map to the right tool calls and parse
      back; write-back marker round-trips; malformed output handled

## 6. Docs

- [ ] 6.1 Document the Claude-MCP backend in `INSTRUCTIONS.md`: config, the Anthropic key + scoped MCP
      auth, webhook-only sync, the **honest credential nuance** (scoped, not zero-credential), and the
      **scoped Jira OAuth** non-LLM alternative
