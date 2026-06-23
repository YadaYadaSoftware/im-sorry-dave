# Tasks

## 1. Directory store and persistence

- [ ] 1.1 Add an `IdentityLink` domain entity (`SlackUserId`, `JiraAccountId`, `DisplayName?`,
      `Source` enum `SelfService|Admin|IdpSync`, `Verified`, `CreatedUtc`, `UpdatedUtc`)
- [ ] 1.2 Add `DbSet<IdentityLink>` to `JiraSyncDbContext` with a unique index on `SlackUserId` and a
      unique index on `JiraAccountId`; add the EF migration
- [ ] 1.3 Add `IIdentityDirectory` (lookup by `accountId` / by Slack id, and upsert) backed by the
      `DbContext`, with upsert replacing the existing row for either unique key

## 2. Resolver-chain integration

- [ ] 2.1 Add `DirectoryIdentityResolver : IJiraSlackIdentityResolver` that resolves by
      `JiraUserRef.AccountId` via `IIdentityDirectory`, returning null when absent
- [ ] 2.2 In `ServiceCollectionExtensions.AddJiraSyncCore`, register `IIdentityDirectory` and register
      `DirectoryIdentityResolver` **before** `ConfigMapIdentityResolver` in the resolver chain

## 3. Self-service `/linkme`

- [ ] 3.1 Add the `/linkme` slash command to the `/slack` group, signature-verified via the existing
      `VerifyAsync` path; take the Slack id from the signed payload (`user_id`)
- [ ] 3.2 Verify the asserted Jira account resolves to a real Jira user, then upsert a
      `Verified=true`, `Source=SelfService` entry; ACK immediately and report via `response_url`

## 4. Admin / bulk import

- [ ] 4.1 Add an admin API endpoint (alongside existing admin endpoints) to upsert directory entries
      sourced as admin / verified
- [ ] 4.2 Add a console command to bulk-import / edit directory entries from a list of
      (`accountId`, Slack user id, optional `displayName`) pairs

## 5. Deferred IdP sync seam

- [ ] 5.1 Document the future `IdpDirectorySyncService` (Okta/Entra/SCIM) seam writing
      `Source=IdpSync` entries through `IIdentityDirectory`; no implementation in this change

## 6. Tests

- [ ] 6.1 Directory entry overrides the config map; config map still resolves when the directory is
      empty
- [ ] 6.2 `/linkme` upserts a verified self-service entry; a non-resolving Jira account writes nothing
- [ ] 6.3 Bulk import upserts admin-verified entries and replaces an existing link
- [ ] 6.4 Upsert enforces one-to-one on both Slack id and Jira `accountId` (re-link replaces)

## 7. Deploy & verify

- [ ] 7.1 Apply the migration; have a user run `/linkme` and confirm a channel invite/mention resolves
      them with no `Slack:UserMap` edit
- [ ] 7.2 Docs: note the identity directory, `/linkme`, and the directory-before-config-map precedence
      in the README Slack section
