## Context

Jira access is already abstracted behind `IJiraClient` (`GetIssueAsync`, `SearchAsync`,
`AddCommentAsync`, `UpdateCommentAsync`, `GetCommentAsync`), chosen in DI by a single
`useFake ? Fake : Real` branch. Three call sites use it: `ReconciliationRunner` (backfill/sweep
reads), `WriteBackSender` (comment writes), and `WebhookProcessor` (fetch a comment's ADF for
mentions). Inbound Jira **webhooks need no key** (Jira pushes; we verify a shared secret). The
customer trusts a Jira ↔ Claude integration but not a direct Jira API token on our service. This
change adds a Claude-MCP backend behind the same interface so Jira is reached through Claude.

## Goals / Non-Goals

**Goals:**
- A selectable `IJiraClient` backend that reads/writes Jira via Claude + the Atlassian MCP server.
- Keep the platform free of a **broad Jira API token**; it holds an Anthropic key (+ scoped MCP auth).
- No change to the rest of the platform (sync, outbox, webhooks, Slack).
- Keep the LLM out of the hot path: webhook-only steady-state sync; Claude only for backfill/
  reconcile/write-back.

**Non-Goals:**
- Replacing the REST backend (it stays; this is additive and selectable).
- LLM-mediated bulk export as the primary sync mechanism (explicitly avoided — see trade-offs).
- Building the Atlassian MCP server (it's Atlassian-hosted) or its OAuth setup UI.

## Decisions

### A third `IJiraClient` backend, selected by `Jira:Backend`
**Decision:** Add `ClaudeMcpJiraClient : IJiraClient`. Replace `Jira:UseFake` (bool) with
`Jira:Backend` = `Fake | Rest | ClaudeMcp` (default preserves today: `Rest` when credentials present,
else `Fake`; `UseFake=true` still maps to `Fake` for back-compat). DI registers the chosen backend.
*Why:* the interface seam already exists; nothing downstream changes. *Alternative — fork the whole
service:* rejected (the interface is the point).

### Reads/writes go through the Anthropic Messages API + Atlassian MCP
**Decision:** `ClaudeMcpJiraClient` calls the **Anthropic Messages API with the MCP connector**
configured for the **Atlassian Remote MCP server**, instructing Claude to invoke the specific
Atlassian tool (get issue, search by JQL, add/edit comment) and return a **structured** result we
parse back into `JiraIssueData` / comment ids. Tool use is constrained and the prompt asks for exact
fields, to keep results deterministic. *Why:* this is the supported path for an app to use a remote
MCP server via Claude. *Alternative — drive claude.ai's stored connection:* not available to the API
(the API takes MCP config per request), so rejected.

### Webhook-only steady state; Claude only for backfill / reconcile / write-back
**Decision:** With the ClaudeMcp backend, **ongoing sync relies on webhooks** (no key, real-time,
deterministic). The LLM is used only for: the **one-time backfill**, the **reconciliation sweep**
(low frequency, or disabled), and **write-back** (low-volume comments). A config flag can disable
backfill/reconcile entirely for a pure webhook-only deployment. *Why:* bulk reads via an LLM are the
weak spot (latency, token cost, non-determinism, pagination/hallucination risk); webhooks already
make them unnecessary for steady state. *Alternative — route every read through Claude:* rejected
(slow, costly, fragile).

### Idempotent write-back preserved
**Decision:** Write-back still embeds the `[managed-record:…]` marker in the comment body; the MCP
backend asks Claude to post that exact body (and to edit by comment id on resubmission). The outbox,
retries, and idempotency logic are unchanged. *Why:* correctness must not depend on the backend.

### Secrets and the honest credential nuance
**Decision:** The platform holds an **Anthropic API key** (`/jira-sync/Anthropic/ApiKey`, via the
secrets convention) plus the **Atlassian MCP endpoint + its auth** (a scoped, revocable OAuth grant
to the Atlassian MCP server — *not* a broad personal Jira API token).

**Honest nuance to document:** using the Anthropic *API* MCP connector, the platform must pass the
MCP server's auth token on each call — so the platform still handles a *Jira-side credential*, just a
**scoped, revocable, auditable** one rather than a broad API token. This satisfies "don't hand out a
broad API key" strongly; it does **not** make the platform hold *zero* Jira credential. True
zero-credential would require an intermediary that owns the MCP/OAuth and never exposes a token to
us — out of scope here. State this plainly so the customer chooses with eyes open.

### Considered alternative: scoped OAuth / Connect app (no LLM in the data path)
**Decision:** Document, but do not build here, a **scoped Jira credential** — a dedicated service
account or an Atlassian Connect/Forge/OAuth-2.0 app limited to the one project with Browse + Add
Comments, revocable and auditable. *Why offer it:* it addresses the same "no broad API key" concern
**without** the LLM trade-offs (deterministic REST, no token cost), and is often the better enterprise
answer. The customer can pick: Claude-MCP (reuse the Claude trust) vs. scoped-OAuth (deterministic).

## Risks / Trade-offs

- [LLM non-determinism on reads] → webhook-only steady state; Claude only for occasional backfill/
  reconcile; validate parsed results and fall back/log on malformed output.
- [Token cost & latency] → low-volume usage by design; cache nothing sensitive; batch where possible.
- [Credential nuance not fully zero-credential] → documented; scoped OAuth offered as the alternative.
- [MCP/Anthropic API surface is evolving] → isolate it in one client class behind `IJiraClient`;
  pin/guard the beta surface; the REST backend remains the fallback.
- [Write-back fidelity] → assert the marker round-trips; keep the REST backend available to compare.

## Migration Plan

1. Add `Jira:Backend` selector (back-compatible with `UseFake`) + `AnthropicOptions`.
2. Implement `ClaudeMcpJiraClient` (Anthropic Messages API + Atlassian MCP) for get/search/comment.
3. Register it in DI when `Jira:Backend = ClaudeMcp`; add `/jira-sync/Anthropic/ApiKey` + MCP config.
4. Default to **webhook-only** sync for this backend (flag to enable Claude backfill/reconcile).
5. Tests: backend selection; a stubbed MCP/Anthropic transport verifying get/search/add/edit map to
   the right tool calls and parse back correctly; write-back marker round-trip.
6. Document the credential model (and the scoped-OAuth alternative) in `INSTRUCTIONS.md`.
- *Rollback:* set `Jira:Backend = Rest` (or `Fake`); nothing else changes.

## Open Questions

- Exact Atlassian MCP tool names/shapes and the Anthropic MCP-connector beta surface — pin during
  implementation; isolate in the one client class.
- Whether to disable backfill/reconcile entirely by default for this backend, or keep them via Claude
  — lean webhook-only, make it a flag.
