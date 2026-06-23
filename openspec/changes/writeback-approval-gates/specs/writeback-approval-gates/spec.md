## ADDED Requirements

### Requirement: Confidence auto-confirm gate

The system SHALL auto-confirm a `SummaryCandidate` — writing it back through
`IConversationSummarizer.ConfirmAsync` / `IWriteBackService.SubmitAsync` without a human Confirm click —
when the candidate's `Confidence` meets or exceeds the configured auto-confirm threshold resolved for its
`Kind`, and SHALL otherwise leave the candidate to the existing manual-confirm flow. The threshold SHALL
resolve to the per-`Kind` value when configured, else the global default, else treated as disabled (no
auto-confirm). An auto-confirmed write-back SHALL be attributed to a system actor distinct from a human
confirmer.

#### Scenario: Candidate above threshold is auto-confirmed

- **WHEN** a candidate's `Confidence` is greater than or equal to the auto-confirm threshold resolved for
  its `Kind` and no required-approver rule matches it
- **THEN** the system confirms and submits it to Jira write-back without waiting for a human click
- **AND** the candidate's card is rendered as already-written rather than offering Confirm/Reject buttons

#### Scenario: Candidate below threshold still requires manual confirmation

- **WHEN** a candidate's `Confidence` is below the auto-confirm threshold resolved for its `Kind`
- **THEN** the system posts the interactive Confirm/Reject card and writes back only after a human Confirm

#### Scenario: Per-Kind threshold overrides the global default

- **WHEN** a per-`Kind` auto-confirm threshold is configured for that candidate's `Kind`
- **THEN** the system uses the per-`Kind` threshold instead of the global default when deciding to
  auto-confirm

#### Scenario: Auto-confirm disabled by default

- **WHEN** no auto-confirm threshold is configured for a candidate's `Kind` and no global default is set
- **THEN** the system does not auto-confirm the candidate and requires a manual human Confirm as today

### Requirement: Required-approver gate

The system SHALL enforce that a `SummaryCandidate` matching a configured required-approver rule is
confirmed only by the named approver — a specific Slack user or a member of a named Slack group — and
SHALL reject a Confirm action from any other user, leaving the candidate's card pending. A rule MAY match
by `Kind`, by the Jira project derived from `RecordIdentity`, or both, with the first matching rule in
configured order winning. The acting user's identity and group membership SHALL be resolved via
`IJiraSlackIdentityResolver`. When the configured approver cannot be resolved, the system SHALL fail
closed (leave the candidate pending) and surface a clear message.

#### Scenario: Approver confirms a gated candidate

- **WHEN** a candidate matches a required-approver rule and the user clicking Confirm at
  `/slack/interactivity` is the named approver or a member of the named approver group
- **THEN** the system accepts the confirmation and submits the candidate to Jira write-back

#### Scenario: Non-approver confirm is rejected

- **WHEN** a candidate matches a required-approver rule and a user who is not the named approver clicks
  Confirm
- **THEN** the system rejects the action, does not write back, and leaves the candidate's card pending for
  the named approver

#### Scenario: Approver gate takes precedence over auto-confirm

- **WHEN** a candidate's `Confidence` clears the auto-confirm threshold but it also matches a
  required-approver rule
- **THEN** the system does not auto-confirm it and waits for the named approver to confirm

#### Scenario: First matching rule wins

- **WHEN** a candidate satisfies the conditions of more than one configured approver rule
- **THEN** the system applies the first matching rule in configured order to determine the required
  approver

#### Scenario: Unresolvable approver fails closed

- **WHEN** a candidate matches a required-approver rule whose configured approver cannot be resolved
- **THEN** the system does not write back, leaves the candidate pending, and surfaces a message indicating
  the approver could not be resolved

### Requirement: Safe default behavior when gates are unconfigured

The system SHALL behave identically to the current manual-confirm-everything flow when no auto-confirm
threshold and no required-approver rules are configured: every `SummaryCandidate` SHALL be posted as an
interactive Confirm/Reject card and SHALL be written back only after a human Confirm, which any channel
member may perform.

#### Scenario: No gate configuration preserves today's flow

- **WHEN** neither an auto-confirm threshold nor any required-approver rule is configured
- **THEN** every candidate requires a manual human Confirm click before write-back
- **AND** any channel member is permitted to perform that confirmation
