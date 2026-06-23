## Context

Identity resolution flows through `IJiraSlackIdentityResolver` — a pluggable chain registered as a DI
`IEnumerable` and tried in order, first non-null wins. `ConfigMapIdentityResolver` is the only
implementation; it reads `SlackOptions.UserMap` (Jira `accountId` OR `displayName` → Slack id). Every
people-touching feature (channel invites for creator/assignee/watchers, mention invites, mention
welcomes, notifications) calls the chain and skips anyone it can't resolve. The interface already
anticipates richer providers: `JiraUserRef(AccountId, DisplayName, Email)` carries `Email` (null on
Jira Cloud) precisely so a Data-Center/admin resolver could use `ISlackClient.LookupUserIdByEmailAsync`.

This change adds a *managed store* as a higher-precedence provider in that same chain, so the map
stops being hand-edited and instead grows from real user/admin action. The store lives in the existing
`JiraSyncDbContext` (SQLite), and `/linkme` joins the existing signature-verified `/slack` endpoints.

## Goals / Non-Goals

**Goals:**
- A persisted Jira↔Slack directory (`accountId` ↔ Slack id) that any consumer benefits from with **no
  code change**, via a new resolver placed ahead of the config map.
- Self-service population: `/linkme` lets a Slack user link their own Jira account, verified.
- Admin/bulk population: a console command and an admin API endpoint to import/edit entries.
- A clear, deferred seam for an IdP/SCIM sync provider that writes the same store.

**Non-Goals:**
- Removing `ConfigMapIdentityResolver` or `Slack:UserMap` — they stay as a static fallback.
- Implementing the Okta/Entra/SCIM sync now (this change only defines the provider seam).
- Changing `IJiraSlackIdentityResolver`, `ISlackClient`, or any consumer of the chain.
- A full identity UI — `/linkme` plus admin import is the surface.

## Decisions

### Persist a directory entity in the existing store
**Decision:** Add an `IdentityLink` domain entity — `SlackUserId`, `JiraAccountId`, `DisplayName?`,
`Source` (`SelfService` | `Admin` | `IdpSync`), `Verified` (bool), and `CreatedUtc`/`UpdatedUtc` —
exposed as `DbSet<IdentityLink>` on `JiraSyncDbContext`, with a **unique index on `SlackUserId`** and a
**unique index on `JiraAccountId`** so the mapping stays one-to-one in each direction, plus an EF
migration. *Why the existing SQLite store:* the platform already persists `WorkItem`, `ResourceMapping`,
captured messages, etc. there — no new infrastructure. *Why one-to-one both ways:* a Jira account maps
to exactly one Slack user and vice-versa; an upsert replaces a stale link rather than duplicating.

### `IIdentityDirectory` store + `DirectoryIdentityResolver`
**Decision:** Introduce `IIdentityDirectory` (lookup + upsert over `IdentityLink`) and a
`DirectoryIdentityResolver : IJiraSlackIdentityResolver` that, given a `JiraUserRef`, returns the Slack
id for a matching **`AccountId`** (the stable Jira Cloud key), or null when absent. Keep the resolver
thin — all storage logic sits in `IIdentityDirectory` so `/linkme`, admin import, and the future IdP
sync share one write path. *Why resolve by `AccountId` only:* it's the durable identifier; `DisplayName`
matching stays the config map's job.

### Precedence: directory ahead of the config map
**Decision:** In `ServiceCollectionExtensions.AddJiraSyncCore`, register
`DirectoryIdentityResolver` **before** `ConfigMapIdentityResolver` in the `IEnumerable` chain. First
non-null wins, so a managed/verified entry overrides the static map, and the config map remains the
fallback for anyone not yet in the directory. *Why directory-first:* the directory reflects real,
self-asserted/verified or admin-curated identity; the static map is the legacy default. *Alternative —
config-map-first:* rejected (it would let a stale hand-edited entry shadow a verified self-link).

### `/linkme` self-service flow + verification
**Decision:** Add `/linkme` to the `/slack` slash-command surface, signature-verified via the same
`VerifyAsync` path as `/post`. The command identifies the caller's **Slack id** from the slash payload
(`user_id`) and links it to a Jira account the user asserts (account id or, where the form allows, an
email/lookup resolving to an account id). To prevent a user claiming someone else's account, gate the
write behind a **lightweight verification**: confirm the asserted Jira account resolves to a real Jira
user (via the Jira client) and that the Slack id is the authenticated caller (Slack already signs the
request and supplies `user_id`), then upsert a `Verified=true`, `Source=SelfService` entry. ACK
immediately (Slack's 3s window); do slow Jira lookups out-of-band and report via the command's
`response_url`, matching the existing `/post` pattern. *Why Slack-id-from-payload:* the signed payload
authenticates *which Slack user* is linking, so a self-link can't spoof the Slack side.

### Admin / bulk import
**Decision:** Provide a console command and an admin API endpoint (alongside the existing admin
endpoints) that upsert `IdentityLink` rows with `Source=Admin`. Bulk import accepts a list of
(`accountId`, `slackUserId`, optional `displayName`) pairs. Admin entries are treated as `Verified`
(an operator curated them). *Why both console and API:* console suits one-off seeding from an exported
roster; the API suits automation.

### IdP / SCIM sync as a deferred provider
**Decision:** Define the seam only: a future `IdpDirectorySyncService` (Okta/Entra/SCIM) would
periodically write `Source=IdpSync` entries through the **same `IIdentityDirectory`** write path, so it
needs no resolver or consumer changes. Not implemented in this change. *Why deferred:* it requires an
enterprise IdP connection and credentials (SSM) that aren't in scope; carving the seam now keeps the
store and precedence rules forward-compatible.

## Risks / Trade-offs

- [Self-link spoofing the Jira side] → a user could assert a Jira account that isn't theirs. Mitigate
  with the verification gate (account must resolve to a real Jira user); admins can correct via import.
  Stronger proof (e.g. an out-of-band Jira-side token) is possible later without changing the store.
- [Precedence surprises] → a verified directory entry overrides the static map; document that the map
  is now a fallback. Operators removing someone from the map no longer un-resolves them if the
  directory has them (the intended behavior).
- [One-to-one constraint conflicts] → re-linking a Slack id (or Jira account) to a new counterpart is
  an upsert that replaces the prior row; the unique indexes prevent silent duplicates.
- [Migration on existing DBs] → ship the EF migration; the table is additive and empty until populated.

## Migration Plan

1. Add the `IdentityLink` entity, `DbSet`, unique indexes, and EF migration.
2. Add `IIdentityDirectory` + `DirectoryIdentityResolver`; register it **before**
   `ConfigMapIdentityResolver`.
3. Add `/linkme` (verified self-service) and the admin/console bulk import over `IIdentityDirectory`.
4. Tests: directory hit overrides the config map; config map still resolves when the directory is
   empty; `/linkme` upserts a verified entry; bulk import upserts admin entries.
5. Deploy; have a user run `/linkme`, then confirm a channel invite/mention now resolves them without
   any `Slack:UserMap` edit.

## Open Questions

- Verification strength for `/linkme` — start with "account resolves to a real Jira user + signed
  Slack caller"; tighten to an out-of-band Jira proof if spoofing proves a concern.
- Whether to expose a `/unlinkme` self-service removal now or defer to admin edit (lean: defer).
