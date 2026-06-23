## Why

Jira↔Slack identity is the linchpin of every people-touching feature — channel invites for the
creator/assignee/watchers, mention invites, mention welcomes, and notifications all go through the
`IJiraSlackIdentityResolver` chain. Today that chain has a single implementation,
`ConfigMapIdentityResolver`, backed by a static `Slack:UserMap` hand-edited into appsettings (Jira
`accountId` or `displayName` → Slack user id). That map is a chore to maintain, goes stale, and
silently drops anyone who isn't in it (the invite is skipped). Jira Cloud hides user email, so the
obvious `ISlackClient.LookupUserIdByEmailAsync` fallback doesn't work there — there is no automatic
path, only the manual map. The platform needs a **managed, self-healing identity directory** that the
people themselves (and admins) populate, so resolution improves over time without anyone editing
config.

## What Changes

- **Persisted identity directory.** Introduce a stored Jira↔Slack mapping
  (`accountId` ↔ Slack user id, with optional display name and provenance/verification metadata),
  living in the existing SQLite store alongside the other entities.
- **Self-service `/linkme`.** Add a `/linkme` Slack slash command under the existing `/slack` group:
  a Slack user links their own Jira account, with a lightweight verification step, writing a
  `verified` directory entry keyed by their Slack id.
- **Admin / bulk import.** Provide a console command and an admin API endpoint to bulk-import or
  edit directory entries (e.g. seeding from an exported roster), marked as admin-provenance.
- **Resolver-chain integration.** Add `DirectoryIdentityResolver` and register it **ahead of**
  `ConfigMapIdentityResolver` in the existing DI `IEnumerable<IJiraSlackIdentityResolver>` chain, so
  the directory is consulted first and the config map remains a static fallback. **No consumer
  changes** — channel invites, mention invites, mention welcomes, and notifications all benefit
  automatically because they already resolve through the chain.
- **Deferred IdP/SCIM provider.** Define (but do not implement now) an IdP/directory-sourced sync
  (Okta/Entra/SCIM) as a future provider that populates the same directory store on a schedule.

## Capabilities

### New Capabilities
- `identity-directory`: a managed, persisted Jira↔Slack identity directory populated by self-service
  (`/linkme`), admin/bulk import, and (deferred) IdP sync, surfaced to the existing resolver chain via
  a `DirectoryIdentityResolver` that takes precedence over the static config map.

## Impact

- `src/SorryDave.JiraSync.Core/Domain` — new `IdentityLink` entity (Slack id, Jira accountId, display
  name, source, verification state, timestamps).
- `src/SorryDave.JiraSync.Core/Persistence` — `DbSet<IdentityLink>`, unique indexes on Slack id and
  Jira accountId, and an EF migration.
- `src/SorryDave.JiraSync.Core/Slack` — `IIdentityDirectory` store + `DirectoryIdentityResolver`
  (new `IJiraSlackIdentityResolver`).
- `src/SorryDave.JiraSync.Core/DependencyInjection/ServiceCollectionExtensions.cs` — register the
  directory store and `DirectoryIdentityResolver` **before** `ConfigMapIdentityResolver`.
- `src/SorryDave.JiraSync.Api/Endpoints/SlackEventEndpoints.cs` — `/linkme` slash command (signature-
  verified like the others); admin import endpoint alongside the existing admin endpoints.
- Console — a directory import/edit command.
- No change to `IJiraSlackIdentityResolver`, `ISlackClient`, or any current consumer. Builds on the
  `slack-channel-lifecycle` membership requirement (which depends on identity resolution) without
  modifying it.
