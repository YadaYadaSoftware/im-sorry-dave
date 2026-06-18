## Why

Team conversations about work happen in Slack, but there is no consistent, discoverable place to discuss a specific work item. We want a dedicated Slack channel per Jira work item so every discussion has a home, and so the platform has a well-defined surface from which to capture decisions and answers for write-back to Jira.

## What Changes

- Automatically provision a Slack channel for each tracked Jira work item, named and described from the work item.
- Seed each new channel with the work item's context (key, summary, type, status, assignee, link to Jira) and keep that context current as Jira changes.
- Maintain a durable, unique link between the Slack channel and the work item (stored via the core mapping store).
- Manage channel lifecycle: archive (not delete) the channel when the work item is completed/closed, and re-activate if the item reopens.
- Reflect key Jira status/assignee changes into the channel (topic and/or a status message) so Slack stays informed without leaving Slack.

## Capabilities

### New Capabilities
- `slack-channel-lifecycle`: Create, name, seed, archive, and re-activate a Slack channel per Jira work item and manage membership.
- `slack-jira-linkage`: Maintain the bidirectional channel ↔ work-item link and keep work-item context reflected into the channel.

### Modified Capabilities
<!-- None - new capabilities. Depends on jira-work-item-sync from jira-sync-core. -->

## Impact

- New Slack App with bot token scopes for channel management (`channels:manage`/`groups:write`, `channels:read`, `chat:write`, `conversations` admin as needed) and Slack Web API client.
- Depends on `jira-work-item-sync` (work-item model + mapping store) and consumes Jira status-change events to update channels.
- Provides the channel surface consumed by `slack-conversation-summarization`.
- Subject to Slack workspace limits on channel count and naming; archiving (not deletion) is used to stay within retention norms.
