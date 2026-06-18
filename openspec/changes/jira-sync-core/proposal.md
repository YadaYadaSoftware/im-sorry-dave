## Why

The project uses Jira as the single source of truth for work (requirements, status, assignee, questions, and answers), but the actual problem-solving happens in conversations elsewhere. We need a foundation that keeps an internal model of Jira work items in sync and provides a reliable, attributed, idempotent path to write decisions and answers back into Jira. Every other integration (Slack, GitHub, OpenSpec, Claude) depends on this shared core.

## What Changes

- Introduce a backend service (ASP.NET Core) that mirrors Jira work items into an internal store and treats Jira as the authoritative source.
- Ingest Jira changes in near-real-time via Jira webhooks (issue created/updated/deleted, comment created), with a periodic reconciliation sweep as a safety net.
- Provide a write-back capability that posts decisions, question/answer summaries, and status notes back to Jira as structured comments and/or designated fields — idempotently and with clear attribution to the originating conversation.
- Establish the shared platform primitives every other change reuses: the work-item ↔ external-resource mapping store, secret/credential handling, and a webhook intake pipeline with signature verification.
- Define the canonical internal work-item model (key, type, status, assignee, summary, description, linked resources) consumed by downstream capabilities.

## Capabilities

### New Capabilities
- `jira-work-item-sync`: Mirror Jira work items into the internal store, keep them current via webhooks plus reconciliation, and expose the canonical work-item model and resource mapping to other capabilities.
- `jira-decision-writeback`: Push decisions, answers, and summaries back into Jira as idempotent, attributed comments and field updates.

### Modified Capabilities
<!-- None - this is the foundational change; no existing specs. -->

## Impact

- New ASP.NET Core service, persistence store (work items + mappings), and Jira REST/webhook client.
- New external dependency: Jira Cloud (REST API v3 + webhooks) and a service account/API token.
- Shared primitives (mapping store, secrets, webhook intake) are consumed by `slack-channel-provisioning`, `slack-conversation-summarization`, `github-work-item-linking`, and `openspec-jira-linking`.
- Requires outbound network access to Jira and an inbound HTTPS endpoint for webhooks.
