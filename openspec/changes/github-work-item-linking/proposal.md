## Why

Code work in GitHub and tracking in Jira are disconnected: reviewers and stakeholders can't easily see which PR implements a work item, and engineers manually move Jira statuses. We want GitHub activity to automatically link to the right work item, post visibility into Jira and the work item's Slack channel, and drive status transitions so Jira reflects real progress.

## What Changes

- Detect Jira work-item keys in commit messages, branch names, and pull request titles/bodies, and link the PR/commits to the work item.
- Post PR lifecycle updates (opened, review requested, merged, closed) to the work item's Jira issue and its Slack channel.
- Drive Jira status transitions from PR lifecycle: opening a PR moves the item to an in-progress/in-review state; merging moves it toward done — using configurable status mappings and respecting Jira workflow validity.
- Record the GitHub PR ↔ work-item link in the core mapping store.

## Capabilities

### New Capabilities
- `github-pr-linkage`: Detect work-item keys in GitHub activity, link PRs/commits to work items, and post status visibility to Jira and Slack.
- `github-status-automation`: Transition Jira work-item status based on PR lifecycle events using configurable, workflow-valid mappings.

### Modified Capabilities
<!-- None - new capabilities. Depends on jira-sync-core (model + mapping + status transition). -->

## Impact

- New dependency on GitHub (App or webhook + REST API) with repo and pull-request read access and webhook delivery.
- Depends on `jira-work-item-sync` (mapping store, model) and adds the ability to perform Jira status transitions (extends the Jira client from `jira-sync-core`).
- Posts to Slack channels via `slack-jira-linkage` channel resolution.
- Requires a configurable PR-event → Jira-status mapping and tolerance for invalid transitions in a given Jira workflow.
