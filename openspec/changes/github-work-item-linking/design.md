## Context

GitHub holds the code; Jira holds the truth. This change connects them: GitHub activity is linked to work items, surfaced in Jira and Slack, and used to drive Jira status. It depends on `jira-sync-core` (model, mapping store, and an extension to the Jira client for status transitions) and `slack-jira-linkage` (to post in the work item's channel).

## Goals / Non-Goals

**Goals:**
- Reliably detect work-item keys across PR title/body, branch, and commits.
- Link PRs to work items and surface PR lifecycle in Jira + Slack.
- Drive Jira status from PR lifecycle using configurable, workflow-valid mappings.

**Non-Goals:**
- Code review automation, CI orchestration, or merge gating.
- Generating Jira items from GitHub (out of scope; item creation comes from Jira/OpenSpec).
- Owning the Jira write/transition primitives (extends the core Jira client, but the mechanism lives in `jira-sync-core`).

## Decisions

- **Key detection precedence and format.** Detect keys with the standard Jira pattern (`[A-Z][A-Z0-9]+-\d+`) across title, body, branch, and commits; a PR may link to multiple items. Maximizes capture without requiring a rigid convention. *Alternative:* branch-name-only convention — rejected (too easy to miss).
- **GitHub App + webhooks.** Use a GitHub App (or org webhook) delivering `pull_request` and `push` events, verified by secret. Gives least-privilege repo access and reliable delivery. *Alternative:* polling the API — rejected (latency, rate cost).
- **Status mapping is configuration, not code.** A declarative map of PR event → target Jira status, applied only when the transition is valid for the current workflow. Teams differ; hard-coding statuses would break. *Alternative:* fixed statuses — rejected (not portable across Jira projects).
- **Never force invalid transitions.** Resolve available transitions from Jira and skip (with a record) when the target isn't reachable. Avoids corrupting Jira workflow state.
- **Idempotency via event identity.** Each GitHub delivery has a unique id; transitions and posts are keyed to it so redelivery is a no-op. Consistent with the core's idempotency approach.
- **Posts reuse existing surfaces.** PR updates go to Jira via the core write path and to Slack via channel resolution from `slack-jira-linkage`. No new notification surface.

## Risks / Trade-offs

- [False-positive key matches in unrelated text] → Validate against tracked work items; record unresolved keys instead of linking.
- [Invalid/blocked Jira transitions] → Query valid transitions first; skip and record rather than error.
- [Webhook duplication / out-of-order delivery] → Idempotency by delivery id; transitions tolerate already-in-target state.
- [Status automation fighting human edits] → Automation only advances per mapping and skips when already past target; humans retain control (Jira authoritative per core).
- [Multiple PRs per item causing thrash] → Mapping considers event type, not raw count; "done" only on merge, configurable.

## Migration Plan

1. Create the GitHub App, configure `pull_request`/`push` events and the webhook secret.
2. Implement key detection + linking; validate on test PRs.
3. Add PR visibility posts to Jira/Slack.
4. Introduce status automation behind a flag with a conservative default mapping; validate transition validity handling on a test project.
- *Rollback:* disable the status-automation flag (links/posts can remain); remove the webhook to fully disable.

## Open Questions

- Default event→status mapping for this team's Jira workflow.
- Should "review requested" or "approved" also drive status, or only opened/merged/closed?
- How to treat draft PRs and reverted merges.
