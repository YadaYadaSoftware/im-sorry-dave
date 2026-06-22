## Why

The customer is reluctant to hand the platform a Jira API token, but they already trust a **Jira ↔
Claude** connection (Atlassian's Claude/MCP integration). They want Jira accessed **through Claude**
rather than via a direct API key on our service, without losing functionality. The platform's Jira
access is already behind the `IJiraClient` interface (real REST vs. in-memory fake), so a third
backend that performs Jira reads/writes via **Claude + the Atlassian MCP server** drops in without
touching the rest of the platform.

## What Changes

- **A Claude-MCP Jira backend.** Add `ClaudeMcpJiraClient : IJiraClient` that satisfies the same
  contract (`GetIssueAsync`, `SearchAsync`, `AddCommentAsync`, `UpdateCommentAsync`,
  `GetCommentAsync`) by calling the **Anthropic Messages API with the MCP connector** pointed at the
  **Atlassian Remote MCP server** — so Jira is reached through Claude, not a direct REST key on our
  service.
- **Backend selection by config.** Replace the boolean `Jira:UseFake` decision with a
  `Jira:Backend` selector (`Fake` | `Rest` | `ClaudeMcp`), defaulting to today's behavior. The REST
  and fake backends are unchanged.
- **Webhook-only steady-state sync (the key-light default).** Because inbound webhooks already
  deliver full issue/comment events with **no key**, ongoing mirroring needs no read access. With the
  ClaudeMcp backend, the **backfill** and **reconciliation** reads go through Claude (occasional,
  low-volume) — or are disabled in favor of webhook-only sync — keeping the LLM out of the hot path.
- **Write-back through Claude.** The outbox posts comments via the MCP backend (low-volume, a natural
  fit); idempotency markers (`[managed-record:…]`) are preserved in the comment body.
- **Secrets follow the convention.** The platform holds an **Anthropic API key**
  (`/jira-sync/Anthropic/ApiKey`) plus the Atlassian MCP endpoint/auth — **not** a broad Jira API
  token. The credential model and its honest nuance are documented (see design).

## Capabilities

### New Capabilities
- `jira-claude-mcp`: accessing Jira **through Claude + the Atlassian MCP server** as a selectable
  `IJiraClient` backend — read (backfill/reconcile) and write-back routed via Claude, with
  webhook-only sync as the key-light default, so the platform needs no direct Jira API token.

## Impact

- `src/SorryDave.JiraSync.Core/Jira` — new `ClaudeMcpJiraClient` + an Anthropic/MCP client; new
  `AnthropicOptions` (API key, model, MCP server URL/auth).
- `src/SorryDave.JiraSync.Core/Configuration/JiraOptions` — `Backend` selector (supersedes `UseFake`,
  kept back-compatible).
- `src/SorryDave.JiraSync.Core/DependencyInjection/ServiceCollectionExtensions` — register the
  backend chosen by `Jira:Backend`.
- `ReconciliationRunner` (backfill/sweep) and `WriteBackSender` (comments) are **unchanged** — they
  call `IJiraClient`; only the implementation differs.
- Secrets: add `/jira-sync/Anthropic/ApiKey` (already anticipated by `secrets-configuration`).
- **Considered alternative (in design):** a **scoped OAuth / Connect app** Jira credential — narrow,
  revocable, no LLM in the data path — for customers who object to a broad token but want
  deterministic REST access.
