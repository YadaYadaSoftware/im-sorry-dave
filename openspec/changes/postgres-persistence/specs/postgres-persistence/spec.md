## ADDED Requirements

### Requirement: Config-selected persistence provider

The system SHALL select its EF Core persistence provider from configuration — SQLite or PostgreSQL —
defaulting to SQLite, using the same `JiraSyncDbContext` and entities for both. The connection string
SHALL continue to come from `ConnectionStrings:JiraSync`.

#### Scenario: SQLite by default for local/dev

- **WHEN** no persistence provider is configured (or it is set to SQLite)
- **THEN** the system registers the DbContext with the SQLite provider using `ConnectionStrings:JiraSync`

#### Scenario: PostgreSQL selected for production

- **WHEN** the persistence provider is configured as PostgreSQL
- **THEN** the system registers the DbContext with the Npgsql provider using the Postgres connection
  string from `ConnectionStrings:JiraSync`

### Requirement: Provider-specific migrations applied at startup

The system SHALL maintain a separate migration set per provider (the existing SQLite set and a
Postgres set) and SHALL apply the migration set matching the active provider when the service starts,
so the database schema is present on either provider.

#### Scenario: Postgres migrations applied when Postgres is active

- **WHEN** the service starts with the PostgreSQL provider against an unmigrated database
- **THEN** the Postgres migration set is applied and the expected schema exists

#### Scenario: SQLite migrations unaffected

- **WHEN** the service starts with the SQLite provider
- **THEN** the existing SQLite migration set is applied and Postgres migrations are not used

### Requirement: Concurrency-safe write-back outbox

When the PostgreSQL provider is active and more than one instance is running, the system SHALL ensure
each due write-back outbox record is sent by at most one instance, so a new record never produces
duplicate Jira comments. The sender SHALL claim due records atomically (e.g. `SELECT … FOR UPDATE SKIP
LOCKED` or an equivalent lease) before sending.

#### Scenario: Two senders, one due record

- **WHEN** two instances drain the outbox concurrently and a single new write-back record is due
- **THEN** exactly one instance sends it, the other skips the locked record, and only one Jira comment
  is created

#### Scenario: Claimed record not picked up by another instance

- **WHEN** one instance has claimed a due record and is sending it
- **THEN** a concurrent instance does not also claim or send that record

### Requirement: Single-runner periodic background work under concurrency

When more than one instance is running, the system SHALL ensure the periodic reconciliation sweep and
the outbox-drain timer run as a single logical runner at a time (via a lease / leader election), so
the sweep is not executed by every instance in parallel; on the holder's failure another instance
SHALL take over.

#### Scenario: Sweep runs once across instances

- **WHEN** multiple instances are running and a reconciliation sweep is due
- **THEN** only the lease holder runs the sweep for that interval

#### Scenario: Failover to another instance

- **WHEN** the instance holding the worker lease stops
- **THEN** another running instance acquires the lease and resumes the periodic work

### Requirement: Postgres connection string sourced as a secret

The Postgres connection string SHALL be resolved at runtime from the existing secret store (AWS SSM
Parameter Store under `/jira-sync/`, Azure Key Vault) and SHALL NOT be baked into the container image,
the task definition, or source.

#### Scenario: Connection string resolved from the secret store

- **WHEN** the service starts with the PostgreSQL provider
- **THEN** it reads `ConnectionStrings:JiraSync` from the secret store, with no Postgres connection
  string present in the image, task definition, or repository
