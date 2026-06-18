## 1. GitHub app & intake

- [ ] 1.1 Create the GitHub App with repo + pull-request read access
- [ ] 1.2 Add the webhook endpoint for `pull_request` and `push` events with secret verification
- [ ] 1.3 De-duplicate deliveries by GitHub delivery id

## 2. Key detection & linking

- [ ] 2.1 Implement work-item key detection across PR title, body, branch, and commits
- [ ] 2.2 Validate detected keys against tracked work items; record unresolved/unlinked cases
- [ ] 2.3 Record PR ↔ work-item links in the core mapping store (supporting multiple PRs per item)

## 3. Visibility posts

- [ ] 3.1 Post PR opened/merged/closed updates to the work item's Jira issue (via core write path)
- [ ] 3.2 Post the same updates to the work item's Slack channel (via `slack-jira-linkage`)
- [ ] 3.3 Ensure posts are idempotent across redelivered events

## 4. Status automation

- [ ] 4.1 Extend the Jira client to list and execute workflow transitions
- [ ] 4.2 Implement the configurable PR-event → target-status mapping
- [ ] 4.3 Apply transitions only when valid for the current workflow; skip and record otherwise
- [ ] 4.4 Make transitions idempotent and attribute them to the triggering PR

## 5. Validation

- [ ] 5.1 Unit tests for key detection, unresolved-key handling, and mapping/validity logic
- [ ] 5.2 Integration test: open PR with key → link + posts + valid transition; merge → done transition
- [ ] 5.3 Document the event→status mapping configuration and GitHub App setup
