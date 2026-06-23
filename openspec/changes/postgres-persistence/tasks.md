# Tasks

## 1. Dual EF provider selection

- [ ] 1.1 Add a `Persistence:Provider` setting (`Sqlite` | `Postgres`), default `Sqlite`
- [ ] 1.2 Add the Npgsql EF package reference to the Core project
- [ ] 1.3 In `AddJiraSyncCore`, branch the `AddDbContext` callback: `Sqlite` → `UseSqlite`,
      `Postgres` → `UseNpgsql(connectionString, b => b.MigrationsAssembly(<pg>))` (keep
      `ConnectionStrings:JiraSync`)

## 2. Provider-specific migrations

- [ ] 2.1 Generate the Postgres migration set with the Npgsql provider into its own output
      directory / migrations assembly (keep the existing SQLite set)
- [ ] 2.2 Apply the active provider's migration set at startup (`Program.cs`)
- [ ] 2.3 Document the dual-provider migration workflow (one migration per provider per schema change)

## 3. Concurrency-safe outbox sender

- [ ] 3.1 In `WriteBackSender`, claim due records atomically on Postgres (`FOR UPDATE SKIP LOCKED` /
      a `ClaimedUntil` lease + owner) before sending; retain the SQLite path
- [ ] 3.2 Ensure a sent record's `JiraCommentId` / edit-in-place idempotency still holds under the
      claim
- [ ] 3.3 Test: two concurrent senders, one due record → exactly one send, one Jira comment

## 4. Single-runner periodic work

- [ ] 4.1 Add a worker lease / leader mechanism (leased row with conditional update, or advisory lock)
- [ ] 4.2 Gate the reconciliation sweep and the outbox-drain timer behind the lease; no-op on SQLite
- [ ] 4.3 Test: sweep runs once across instances; lease fails over when the holder stops

## 5. Connection string as a secret

- [ ] 5.1 Provision the Postgres `ConnectionStrings:JiraSync` as a secret under `/jira-sync/` (and
      Azure Key Vault); confirm nothing in image / task definition / source

## 6. Deployment spec & verify

- [ ] 6.1 Narrow the aws-deployment single-instance requirement to the SQLite provider
- [ ] 6.2 Verify: with Postgres + concurrency-safe workers, run two instances and confirm no
      double-send and no duplicated sweep (the follow-on deploy-config change can then flip instance
      count / enable blue-green)
- [ ] 6.3 Docs: note the `Persistence:Provider` setting and the Postgres production path in the README
