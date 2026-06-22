## ADDED Requirements

### Requirement: Selectable Jira access backend

The system SHALL allow the Jira access backend to be selected by configuration — an in-memory fake,
the direct REST client, or a Claude-MCP client — without changing the rest of the platform, which
accesses Jira only through the `IJiraClient` contract. The default selection SHALL preserve existing
behavior.

#### Scenario: Backend chosen by configuration

- **WHEN** the Jira backend is set to `ClaudeMcp` (resp. `Rest`, `Fake`)
- **THEN** the platform uses that backend for all Jira reads and writes, and the sync, outbox, and
  webhook components are unchanged

#### Scenario: Default preserves existing behavior

- **WHEN** no backend is explicitly configured
- **THEN** the platform behaves as before (REST when Jira credentials are present, otherwise the fake)

### Requirement: Jira reads and writes via Claude and the Atlassian MCP server

When the Claude-MCP backend is selected, the system SHALL perform Jira reads (get issue, search) and
writes (add/edit comment) by calling Claude with the Atlassian MCP server, rather than by holding a
direct Jira API token, and SHALL parse the results back into the platform's work-item and comment
model.

#### Scenario: Read an issue via Claude

- **WHEN** the platform needs a Jira issue or a JQL result under the Claude-MCP backend
- **THEN** it obtains the data through Claude + the Atlassian MCP server and maps it into the
  work-item model

#### Scenario: Write a comment via Claude

- **WHEN** the outbox delivers a write-back under the Claude-MCP backend
- **THEN** the comment (including its idempotency marker) is posted through Claude + the Atlassian MCP
  server, and a resubmission edits the same comment

### Requirement: Webhook-only steady-state sync without a Jira API token

Under the Claude-MCP backend the system SHALL keep mirrored work items current from inbound webhooks
alone (which require no Jira API token), using Claude only for the initial backfill and the
reconciliation sweep, which SHALL be occasional and configurable (including fully disabled in favor
of webhook-only sync).

#### Scenario: Ongoing sync needs no read access

- **WHEN** issue/comment webhooks arrive under the Claude-MCP backend
- **THEN** the platform updates its mirror from the webhook payloads without any Jira API read

#### Scenario: Backfill is occasional and optional

- **WHEN** the operator runs a backfill or the reconciliation sweep under the Claude-MCP backend
- **THEN** those reads go through Claude, and they can be disabled so the deployment runs webhook-only

### Requirement: The platform holds no broad Jira API token

Under the Claude-MCP backend the platform SHALL NOT require a broad Jira API token; it SHALL use an
Anthropic API key (sourced via the secrets convention) and the Atlassian MCP endpoint with a scoped,
revocable credential. The documentation SHALL state clearly that the platform still handles a scoped
Jira-side credential for the MCP server (it is not zero-credential), and SHALL describe the scoped
Jira OAuth credential as a non-LLM alternative.

#### Scenario: No broad Jira API token on the platform

- **WHEN** the platform runs under the Claude-MCP backend
- **THEN** it is configured with an Anthropic API key and scoped MCP auth, and no broad Jira API
  token is present in its configuration or secrets

#### Scenario: Credential model documented honestly

- **WHEN** an administrator reads the backend's documentation
- **THEN** it explains that a scoped, revocable MCP credential is still handled (not zero-credential),
  and offers a scoped Jira OAuth credential as a deterministic, no-LLM alternative
