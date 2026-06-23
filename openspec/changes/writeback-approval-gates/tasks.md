# Tasks

## 1. Configuration model

- [ ] 1.1 Add an auto-confirm threshold section to `SlackOptions`: a global default plus an optional
      per-`Kind` map (values in [0,1]); bind from SSM with empty defaults (gates off)
- [ ] 1.2 Add an ordered list of required-approver rules to `SlackOptions`: each with optional `Kind`,
      optional Jira `Project`, and a required `Approver` (Slack user id or group id)
- [ ] 1.3 Validate config on bind (thresholds in range, approver ids non-empty); log unusable rules

## 2. Gate evaluation in the confirm path

- [ ] 2.1 Add a write-back gate evaluator that, given a `SummaryCandidate` (Confidence, Kind,
      RecordIdentity-derived project) and config, returns: auto-confirm / manual / approver-required
- [ ] 2.2 Resolve the threshold per `Kind` (per-Kind → global → disabled) and the first-matching approver
      rule; enforce approver precedence over auto-confirm
- [ ] 2.3 Call the evaluator inside `IConversationSummarizer.ConfirmAsync` immediately before
      `IWriteBackService.SubmitAsync`

## 3. Auto-confirm flow

- [ ] 3.1 After cards would be posted, run the system/auto confirm path for candidates whose Kind+score
      clear the threshold and that match no approver rule, attributed to a system actor
- [ ] 3.2 Ensure auto-confirm reuses the existing idempotent `SubmitAsync` path (no parallel write route)

## 4. Required-approver enforcement at interactivity

- [ ] 4.1 In the `/slack/interactivity` Confirm handler, when the candidate matches an approver rule,
      resolve the acting Slack user via `IJiraSlackIdentityResolver` and check user/group membership
- [ ] 4.2 Reject a non-approver Confirm with an ephemeral message and leave the card pending; fail closed
      when the configured approver cannot be resolved

## 5. Block Kit card presentation

- [ ] 5.1 Render auto-confirmed candidates as already-written (no Confirm/Reject buttons; show confidence)
- [ ] 5.2 Render approver-gated candidates with context text naming the required approver

## 6. Tests

- [ ] 6.1 Auto-confirm: above-threshold (global and per-Kind) writes back without a click; below-threshold
      requires manual confirm
- [ ] 6.2 Required-approver: approver confirm succeeds; non-approver confirm rejected and left pending;
      group-member approver accepted
- [ ] 6.3 Precedence: approver rule blocks auto-confirm even when confidence clears the threshold
- [ ] 6.4 First-matching-rule selection and fail-closed on unresolvable approver
- [ ] 6.5 Off-by-default equivalence: with no config every candidate requires a manual human Confirm

## 7. Deploy & verify

- [ ] 7.1 Deploy with gates off; confirm behavior is unchanged from today
- [ ] 7.2 Enable a per-Kind threshold and an approver rule via SSM in a non-prod project; verify
      auto-confirm and approver enforcement end to end
- [ ] 7.3 Docs: note the auto-confirm thresholds and approver rules in the README Slack/config section
