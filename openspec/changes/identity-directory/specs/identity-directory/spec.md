## ADDED Requirements

### Requirement: Persisted Jira↔Slack identity directory

The system SHALL maintain a persisted directory of Jira↔Slack identity links, each mapping one Jira
`accountId` to one Slack user id, with a display name, a source (self-service, admin, or IdP sync), a
verification state, and timestamps. The directory SHALL enforce that each Slack user id and each Jira
`accountId` appears at most once, treating a re-link as a replacement of the prior entry rather than a
duplicate.

#### Scenario: Linking the same Slack user again replaces the prior entry

- **WHEN** an identity link is written for a Slack user id that already has a directory entry
- **THEN** the system updates the existing entry rather than creating a duplicate
- **AND** the directory still contains exactly one entry for that Slack user id

#### Scenario: Each Jira account maps to a single Slack user

- **WHEN** a Jira `accountId` is already linked to a Slack user and a new link is written for that
  same `accountId`
- **THEN** the system replaces the prior mapping so the `accountId` resolves to the most recent Slack
  user id

### Requirement: Directory takes precedence in the identity resolver chain

The system SHALL resolve Jira→Slack identity through a directory-backed resolver that is consulted
**before** the static configuration-map resolver in the existing resolver chain, so a directory entry
overrides the static map and the static map remains a fallback for users not in the directory. The
directory resolver SHALL match on the Jira `accountId` and SHALL return no result (allowing the chain
to fall through) when the account is not in the directory. Existing consumers of identity resolution
(channel invites, mention invites, mention welcomes, notifications) SHALL benefit from the directory
without any change to how they resolve identity.

#### Scenario: Directory entry overrides the config map

- **WHEN** a Jira `accountId` has both a directory entry and a different value in the static
  `Slack:UserMap`
- **THEN** identity resolution returns the Slack user id from the directory

#### Scenario: Config map still resolves when the directory has no entry

- **WHEN** a Jira `accountId` is absent from the directory but present in the static `Slack:UserMap`
- **THEN** identity resolution falls through to the config-map resolver and returns the mapped Slack
  user id

#### Scenario: Consumers resolve via the directory unchanged

- **WHEN** a user who is in the directory is a candidate for a channel invite, a mention invite, a
  mention welcome, or a notification
- **THEN** the existing feature resolves their Slack id from the directory without any change to that
  feature

### Requirement: Self-service identity linking via `/linkme`

The system SHALL provide a signature-verified `/linkme` Slack slash command that lets the calling
Slack user link their own Slack id to a Jira account they assert. The command SHALL identify the
caller's Slack id from the signed slash-command payload, SHALL verify that the asserted Jira account
resolves to a real Jira user before writing, and SHALL upsert a verified, self-service directory
entry. The command SHALL acknowledge within Slack's response window and report the outcome via the
command's response URL.

#### Scenario: User links their own Jira account

- **WHEN** a Slack user runs `/linkme` asserting a Jira account that resolves to a real Jira user
- **THEN** the system upserts a directory entry linking that user's Slack id to the Jira `accountId`,
  marked verified and sourced as self-service
- **AND** the user's subsequent channel invites and mentions resolve without any `Slack:UserMap` edit

#### Scenario: Asserted Jira account does not resolve

- **WHEN** a Slack user runs `/linkme` asserting a Jira account that does not resolve to a real Jira
  user
- **THEN** the system does not write a directory entry and reports the failure to the caller

#### Scenario: Slack identity comes from the signed payload

- **WHEN** a `/linkme` request is processed
- **THEN** the linked Slack user id is taken from the signature-verified slash-command payload, so a
  caller cannot link a Jira account to a different Slack user

### Requirement: Admin and bulk import of directory entries

The system SHALL allow an administrator to create or edit directory entries in bulk via a console
command and an admin API endpoint, each upserting entries sourced as admin and treated as verified.

#### Scenario: Bulk import seeds the directory

- **WHEN** an administrator imports a list of (`accountId`, Slack user id) pairs
- **THEN** the system upserts a directory entry for each pair, sourced as admin and marked verified

#### Scenario: Admin edit corrects an existing link

- **WHEN** an administrator imports an entry for a Jira `accountId` that already has a directory entry
- **THEN** the system replaces the existing entry with the administrator-supplied Slack user id

### Requirement: Deferred IdP-sourced directory sync

The system SHALL define an IdP/directory-sourced sync (Okta/Entra/SCIM) as a future provider that
populates the same identity directory store through the same write path, sourced as IdP sync, without
requiring changes to the resolver chain or to identity-resolution consumers. This provider is not
implemented in this change.

#### Scenario: IdP-sourced entries use the same store and resolver

- **WHEN** an IdP sync provider is added later and writes identity links to the directory
- **THEN** those entries resolve through the same directory resolver with no change to the resolver
  chain or to existing consumers
