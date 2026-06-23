## Why

Persistence is SQLite on a mounted file share (EFS on AWS, Azure Files later). Because SQLite over a
network filesystem is single-writer, the service is deliberately pinned to **one instance** with
non-overlapping stop-then-start deploys — which forces brief downtime and forbids blue/green. The
background workers (reconciliation sweep, write-back outbox sender) bake in that single-writer
assumption: nothing stops two senders from draining the same outbox row twice. SQLite is the right
default for local/dev, but it is the thing blocking multi-instance, zero-downtime production. A real
networked database (PostgreSQL) removes the single-writer constraint — and is the natural fit for the
"eventually Azure" managed-DB direction.

## What Changes

- **Dual EF persistence provider.** Keep SQLite for local/dev; add **PostgreSQL** (Npgsql) for
  production, selected by config (a `Persistence:Provider` setting, defaulting to `Sqlite`).
  `AddJiraSyncCore` chooses `UseSqlite` vs `UseNpgsql` from that setting instead of always calling
  `UseSqlite`.
- **Provider-specific migration set. — BREAKING for the build/migration workflow.** EF migrations are
  provider-specific; the existing SQLite migrations cannot run on Postgres. Add a **separate Postgres
  migration set** (its own migrations assembly / output directory) and apply the matching set at
  startup for the active provider.
- **Concurrency-safe background workers. — BREAKING of the single-writer assumption.** Make the
  reconciliation sweep and the outbox sender safe to run on **multiple concurrent instances**: claim
  outbox rows with `SELECT … FOR UPDATE SKIP LOCKED` (or an atomic claim/lease) so two senders never
  send the same write-back, and gate the periodic sweep with a lease / leader election so it does not
  run N times in parallel. This is the real work; it is honestly non-trivial.
- **Connection string as a secret.** The Postgres connection string is a secret resolved from the
  existing secret store (SSM under `/jira-sync/`, Key Vault on Azure) — never baked into the image,
  task definition, or source.
- **Unblocks multi-instance + zero-downtime deploys. — BREAKING of the aws-deployment single-instance
  requirement.** With Postgres and concurrency-safe workers, the service MAY run more than one
  instance and deploy with rolling / blue-green (no stop-then-start downtime). The current
  aws-deployment "single-instance run for background workers" requirement is relaxed to apply **only
  to the SQLite provider**.

## Capabilities

### New Capabilities
- `postgres-persistence`: a PostgreSQL EF provider selectable by config alongside SQLite, with a
  Postgres-specific migration set, concurrency-safe background workers (outbox + sweep) safe for
  multiple concurrent instances, and the connection string sourced as a secret.

### Modified Capabilities
- `aws-deployment`: the **single-instance run for background workers** requirement is narrowed — it is
  required for the SQLite provider, but when the PostgreSQL provider is active the deployment MAY run
  multiple instances and use zero-downtime rolling / blue-green deploys.

## Impact

- `src/SorryDave.JiraSync.Core/DependencyInjection/ServiceCollectionExtensions.cs` — select
  `UseSqlite` vs `UseNpgsql` from `Persistence:Provider`; wire the matching migrations assembly.
- `src/SorryDave.JiraSync.Core/Persistence/Migrations` — keep SQLite migrations; add a sibling
  Postgres migration set (separate output dir / migrations assembly).
- `src/SorryDave.JiraSync.Core/WriteBack/WriteBackSender.cs` — claim outbox rows with `FOR UPDATE
  SKIP LOCKED` / atomic lease so concurrent senders don't double-send.
- `src/SorryDave.JiraSync.Core/Sync/ReconciliationRunner.cs` (and the reconciliation/write-back
  hosted services) — gate the periodic sweep with a lease / leader election under concurrency.
- `src/SorryDave.JiraSync.Api/Program.cs` — apply the active provider's migrations at startup.
- New Npgsql package reference; new `ConnectionStrings:JiraSync` (Postgres) secret in SSM / Key Vault.
- `openspec/specs/aws-deployment/spec.md` — single-instance requirement narrowed to SQLite.
- No domain/schema shape change — same entities, a second provider + migration set.
