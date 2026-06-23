## Context

Persistence is EF Core + SQLite. `AddJiraSyncCore` reads `ConnectionStrings:JiraSync` and registers
`services.AddDbContext<JiraSyncDbContext>(o => o.UseSqlite(connectionString))`; the file lives on an
EFS (later Azure Files) mount. SQLite over a network FS is single-writer, so the service is pinned to
one instance with stop-then-start deploys (aws-deployment's "Single-instance run for background
workers"), and the background workers assume that: `WriteBackSender.ProcessDueAsync` reads due outbox
rows and sends them with **no claim/lock**, and the reconciliation/write-back hosted services each run
their scoped runner on a timer. Two instances would double-drain the outbox and run the sweep twice.

This change adds PostgreSQL as a production provider so the service can run multiple concurrent
instances with zero-downtime deploys. SQLite stays the local/dev default. The secrets layer already
resolves the connection string from SSM (`/jira-sync/`) / Key Vault, so the Postgres connection string
is just another secret.

## Goals / Non-Goals

**Goals:**
- A config-selected EF provider: SQLite (default, local/dev) or PostgreSQL (production), same
  `JiraSyncDbContext` and entities.
- A Postgres-specific migration set applied at startup for the active provider.
- Reconciliation sweep + outbox sender **safe under multiple concurrent instances** (no double-send,
  no duplicated sweep).
- Postgres connection string sourced as a secret.
- Enable multi-instance + rolling / blue-green deploys when Postgres is active.

**Non-Goals:**
- Changing the domain model / entity shape (same tables; a second provider).
- Removing SQLite (it stays the local/dev default and the AWS/Azure file-mount option).
- Provisioning the managed Postgres instance itself (deploy/infra concern; this change consumes a
  connection string).
- Data migration of an existing SQLite database into Postgres (greenfield Postgres; one-time
  backfill, if ever needed, is separate).

## Decisions

### Select the provider by config; default SQLite
**Decision:** Add `Persistence:Provider` (`Sqlite` | `Postgres`), default `Sqlite`. In
`AddJiraSyncCore`, branch the `AddDbContext` callback: `Sqlite` → `UseSqlite(connectionString)` (as
today), `Postgres` → `UseNpgsql(connectionString, b => b.MigrationsAssembly(<pg migrations>))`. The
connection string stays `ConnectionStrings:JiraSync`; a Postgres value is an Npgsql connection string.
*Why a setting over sniffing the connection-string scheme:* explicit, testable, and avoids ambiguous
SQLite `Data Source=` strings. *Alternative — sniff scheme:* rejected as implicit.

### Provider-specific migration sets
**Decision:** EF migrations are provider-specific — the SQLite migrations under
`Core/Persistence/Migrations` cannot run on Postgres. Keep them, and add a **separate Postgres
migration set** in its own output directory / migrations assembly, generated with the Npgsql provider.
At startup apply the set that matches the active provider. *Why:* a single migrations folder cannot
serve both providers (column types, snapshot, and SQL diverge). *Trade-off:* schema changes now
require generating two migrations (one per provider) — a known EF multi-provider cost; document it in
the migration workflow.

### Outbox sender safe for concurrent instances (the core work)
**Decision:** Replace the unguarded "read due rows, then send" in `WriteBackSender` with an **atomic
claim**: on Postgres, select due `Pending`/`Retrying` rows with `FOR UPDATE SKIP LOCKED` (limited
batch) inside a transaction and mark them claimed (e.g. a `ClaimedUntil` lease + owner), so a second
instance skips locked rows and never sends the same write-back twice. Idempotency already helps — a
sent record stores its `JiraCommentId` and edits in place on redelivery — but two senders racing a
*new* record would post two comments, so the claim is required, not optional. On SQLite (single
writer) the existing path is retained. *Why SKIP LOCKED:* the standard Postgres pattern for a
multi-consumer queue; no extra infrastructure. *Alternative — external queue (SQS/Service Bus):*
larger rearchitecture, rejected for this change.

### Periodic sweep gated by a lease / leader
**Decision:** The reconciliation sweep and the outbox-drain timer must not run N times in parallel
across instances. Gate each periodic run behind a **lease** (a row in a `worker_leases` table claimed
with an atomic conditional update / advisory lock, renewed on a heartbeat) so at most one instance
runs the sweep at a time; the lease holder is the de-facto leader, and on its death another instance
acquires the lease. *Why a DB lease over a dedicated leader-election service:* reuses the database we
already have, no new dependency. *Trade-off:* a lease adds bookkeeping and a small failover delay
(one lease TTL) — acceptable. On SQLite this is a no-op (single instance already).

### Connection string is a secret
**Decision:** The Postgres `ConnectionStrings:JiraSync` is a **secret** resolved at runtime from the
existing store (SSM `/jira-sync/`, Key Vault on Azure) — never in the image, task definition, or
source. *Why:* it carries credentials; it rides the same secret convention as the Jira token etc.,
needing no new IAM grant.

### Deployment: Postgres unblocks multi-instance + blue/green
**Decision:** With Postgres active and the workers concurrency-safe, the deployment MAY run **multiple
instances** and use **rolling / blue-green** (no stop-then-start downtime). The aws-deployment
"single-instance run for background workers" requirement is narrowed to apply **only when SQLite is
the provider**; with Postgres it does not. *Note:* the AWS deploy today still forbids >1 instance —
flipping the instance count / deploy strategy is a follow-on deploy-config change that depends on this
one; this change makes it **safe**, it does not itself rewire the ECS service.

## Risks / Trade-offs

- [Double-send if the claim is wrong] → the outbox claim (`FOR UPDATE SKIP LOCKED` + lease) is the
  load-bearing piece; cover it with a concurrent-sender test (two senders, one due row → one send).
- [Two-migrations-per-change overhead] → document the dual-provider migration workflow; CI can assert
  both sets build.
- [Lease failover delay] → sweep pauses up to one lease TTL after a holder dies; keep the TTL short.
- [SQLite/Postgres SQL divergence] → some LINQ translates differently (e.g. SQLite already needs
  in-memory `DateTimeOffset` filtering in `WriteBackSender`); verify queries on both providers, prefer
  translatable expressions, keep provider-specific code at the claim boundary.
- [Honesty] → this is not "flip a flag." The provider/migrations are mechanical; making the workers
  concurrency-safe is genuine concurrency work and the bulk of the effort.

## Migration Plan

1. Add `Persistence:Provider`; branch `AddDbContext` to `UseSqlite` / `UseNpgsql`; add the Npgsql
   package. Default `Sqlite` so existing behavior is unchanged.
2. Generate the Postgres migration set (separate assembly/output dir); apply the active provider's set
   at startup.
3. Make `WriteBackSender` claim rows atomically (`FOR UPDATE SKIP LOCKED` / lease) on Postgres; keep
   the SQLite path. Add a concurrent-sender test.
4. Gate the reconciliation sweep + outbox timer behind a DB lease / leader; no-op on SQLite.
5. Provision the Postgres connection string as a secret under `/jira-sync/` (and Key Vault).
6. Narrow the aws-deployment single-instance requirement to SQLite. (Flipping instance count /
   blue-green in the deploy config is a follow-on change.)

## Open Questions

- Lease mechanism: a `worker_leases` row with conditional update, or Postgres advisory locks
  (`pg_advisory_lock`)? Start with a leased row (provider-agnostic, testable on SQLite).
- One Npgsql connection-string secret, or split host/credentials parameters? Start with a single
  connection-string secret to match the existing convention.
