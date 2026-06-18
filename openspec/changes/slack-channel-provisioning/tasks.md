## 1. Slack app setup

- [ ] 1.1 Create the Slack App and request bot scopes for channel management, read, and chat:write
- [ ] 1.2 Add the Slack Web API client and bot-token configuration/secret handling
- [ ] 1.3 Implement Slack API rate-limit handling (`Retry-After` / `rate_limited`)

## 2. Channel provisioning

- [ ] 2.1 Derive a deterministic channel name from the work-item key (Slack-normalized)
- [ ] 2.2 Create the channel and post the initial work-item context message
- [ ] 2.3 Resolve name collisions with a deterministic suffix and record the actual channel
- [ ] 2.4 Make provisioning idempotent (no duplicate channel when one is already linked)

## 3. Linkage

- [ ] 3.1 Record the channel ID ↔ work-item key association in the core mapping store
- [ ] 3.2 Enforce one-channel-to-one-work-item conflict rejection
- [ ] 3.3 Expose resolve-work-item-from-channel for downstream capabilities

## 4. Lifecycle

- [ ] 4.1 Subscribe to work-item created/status/assignee events from `jira-work-item-sync`
- [ ] 4.2 Archive the channel (with closing summary) when the work item closes
- [ ] 4.3 Unarchive and post a re-activation notice when the work item reopens

## 5. Context reflection & membership

- [ ] 5.1 Update channel topic/purpose on status and assignee changes
- [ ] 5.2 Pin or expose the Jira work-item link in the channel
- [ ] 5.3 Resolve Jira→Slack identities (by email) and invite assignee/reporter, skipping unresolved

## 6. Reconciliation & validation

- [ ] 6.1 Periodically reconcile channel links against Slack channel state to detect drift
- [ ] 6.2 Unit tests for deterministic naming, collision suffix, and link uniqueness
- [ ] 6.3 Integration test: work item created → channel provisioned → status change reflected → close archives channel

## 7. Console commands

- [ ] 7.1 Provide `slack provision/archive/unarchive <key>` handlers calling the lifecycle services
- [ ] 7.2 Provide `slack link <key> <channelId>` and `slack channel <key>` handlers over the mapping store
- [ ] 7.3 Honor the global `--dry-run` for channel-mutating commands
