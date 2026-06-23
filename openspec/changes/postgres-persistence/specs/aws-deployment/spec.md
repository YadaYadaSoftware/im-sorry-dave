## MODIFIED Requirements

### Requirement: Single-instance run for background workers

The deployment SHALL run a single instance **when the SQLite persistence provider is active**, so the
reconciliation and write-back background workers retain their single-writer assumption. When the
**PostgreSQL** provider is active and the workers are concurrency-safe, the deployment MAY run more
than one instance and use zero-downtime rolling / blue-green deploys instead of stop-then-start.

#### Scenario: Single instance enforced for SQLite

- **WHEN** the service is deployed with the SQLite provider
- **THEN** exactly one instance runs, so the outbox is drained by a single writer (no concurrent senders)

#### Scenario: Multiple instances allowed for PostgreSQL

- **WHEN** the service is deployed with the PostgreSQL provider and the concurrency-safe workers
- **THEN** the deployment MAY run more than one instance and deploy with zero downtime, because the
  outbox claim and worker lease prevent double-sends and duplicated sweeps
