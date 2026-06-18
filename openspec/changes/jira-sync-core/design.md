## Context

This is the foundational change for a platform that connects Jira (source of truth), Slack (conversation), GitHub (code), OpenSpec (specs), and Claude (summarization). All other changes depend on a shared internal model of work items, a mapping store, and a reliable write-back path. The team has chosen .NET / C# (ASP.NET Core) and an event-driven (webhook) sync model with reconciliation as a safety net.

## Goals / Non-Goals

**Goals:**
- A single ASP.NET Core service that owns the canonical work-item model and the work-item ↔ external-resource mapping.
- Near-real-time ingestion of Jira changes via webhooks, with periodic reconciliation to recover dropped events.
- Idempotent, attributed write-back of decisions/answers/summaries to Jira.
- Reusable primitives (webhook intake with signature verification, secret handling, mapping store) for downstream changes.

**Non-Goals:**
- Producing the summaries/decisions themselves (that is `slack-conversation-summarization` + Claude).
- Slack channel lifecycle, GitHub linking, or OpenSpec linking (separate changes).
- Replacing Jira as a UI; we never build a competing work-tracking surface.

## Decisions

- **Jira is authoritative; we never fight it.** For any field editable in Jira (status, assignee, summary, description), Jira wins on conflict. Write-back is confined to comments and a small set of dedicated custom fields, never to fields a human edits directly. *Alternative considered:* bidirectional field ownership — rejected as a sync-conflict generator.
- **Event-driven + reconciliation.** Jira webhooks drive latency-sensitive updates; a scheduled reconciliation sweep (JQL by `updated >= lastSweep`) closes gaps from dropped/missed webhooks. *Alternative:* polling only — rejected for latency; webhooks only — rejected for reliability.
- **Versioned last-write-wins guarded by Jira `updated` timestamp.** Each record stores the Jira `updated` marker; stale/out-of-order events are discarded. Prevents an older webhook from clobbering newer data.
- **Idempotency via a managed-record marker.** Each write-back carries a stable record identity embedded in a hidden/structured marker in the Jira comment body (and/or an entity-property). Resubmission edits in place. *Alternative:* tracking only by our DB id — kept, but the in-Jira marker makes recovery possible if our store is lost.
- **Outbox + retry queue for write-back.** Records are persisted to an outbox first, then delivered to Jira with backoff; transient failures retry, permanent failures are flagged. Guarantees no decision is lost if Jira is briefly unavailable.
- **Mapping store is the integration hub.** A single table keyed by (resourceType, resourceId) ↔ workItemKey, with uniqueness on resourceId. Downstream changes resolve work items through this store rather than each maintaining their own.
- **Secrets via configuration provider.** Jira API token and webhook secret are read from the platform secret store (e.g., user-secrets in dev, a managed secret store in prod), never committed.
- **Layered configuration.** Settings resolve from committed `appsettings` defaults, overridden by environment variables (with the ASP.NET Core `__` nesting convention for containers/CI) and, in development, user-secrets. The real Jira client is selected only when base URL + email + token are all present; otherwise the in-memory fake client runs so the service is usable with zero setup. Ops configuration is documented in the API's `README.md`.
- **Round-trip verification against real Jira.** A credential-gated integration test exercises the full path (REST fetch → webhook envelope → store → write-back → read-back/edit verification) against a real Jira test project. It reads credentials from user-secrets/env and skips when they are absent, so the default test run needs no Jira access. It intentionally leaves its decision comment on the test issue for manual inspection (each run uses a unique record identity, so runs do not collide).

## Risks / Trade-offs

- [Dropped webhook causes stale data] → Reconciliation sweep + on-read freshness check for critical paths.
- [Jira rate limits during reconciliation or burst write-back] → Respect `Retry-After`, bound concurrency, backoff in the outbox.
- [Idempotency marker stripped by a user editing the comment] → Also persist record identity in our store and (optionally) Jira issue entity properties as a secondary key.
- [Clock skew / equal `updated` timestamps] → Treat equal timestamps as "apply" and rely on field-level idempotency; reconciliation corrects any drift.
- [PII / sensitive content written to Jira] → Write-back content originates from confirmed records; redaction policy is owned by the summarization change.

## Migration Plan

1. Stand up the service and persistence (work items, mappings, outbox) — greenfield, no existing data to migrate.
2. Register Jira webhooks against the deployed HTTPS endpoint; configure the shared secret.
3. Backfill tracked work items via an initial reconciliation sweep over the target project(s).
4. Enable write-back behind a feature flag; validate idempotency on a test issue before broad use.
- *Rollback:* disable webhooks and write-back flag; the internal store is a mirror and can be rebuilt by reconciliation.

## Open Questions

- Which Jira fields (if any) beyond comments should hold decisions/answers — dedicated custom fields vs. comments only?
- Scope of tracked work: entire Jira project(s), specific issue types, or a label/JQL filter?
- Retention policy for soft-deleted (deleted-in-Jira) internal records.
