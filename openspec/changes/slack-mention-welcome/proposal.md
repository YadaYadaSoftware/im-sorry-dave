## Why

When someone is invited to a channel because they were @mentioned (in a comment or the description),
they land in the channel with no context about *why*. The platform already knows the exact text where
they were mentioned — it should post that text as a welcome so the invitee immediately sees what
pulled them in. For a comment/edit-driven invite there is no welcome message today, so this becomes
the welcome message for them.

## What Changes

- **Mention-context welcome message.** When a user is invited to a channel because of a mention, post
  a message in the channel addressed to them that includes the **text of the mention** — the comment
  body (or the description) where they were @mentioned — so they have context on arrival.
- **It becomes the welcome when there isn't one.** For a comment/description-edit invite on an
  existing channel (no welcome message in that moment), this mention-context message **is** the
  welcome. At auto-provision, the description is already posted as the welcome, so the existing
  message serves that role (no duplicate).
- **Plumbing:** capture the mention's surrounding **text** (not just the accountId) — flatten the
  fetched comment ADF / the description — and carry it on the change event so `SlackChannelService`
  can post it when inviting.

## Capabilities

### Modified Capabilities
- `slack-channel-lifecycle`: extend channel **membership** so a mention-driven invite also posts a
  welcome message to the invitee containing the text of the mention.

## Impact

- `src/SorryDave.JiraSync.Core/Jira` — flatten comment ADF to text (reuse `AdfText.Flatten`);
  `IJiraClient.GetCommentMentionsAsync` returns the body text alongside the accountIds (or a sibling
  call).
- `src/SorryDave.JiraSync.Core/Sync` — carry the mention context text on `WorkItemChange` (from the
  comment path and the description-edit path).
- `src/SorryDave.JiraSync.Core/Slack/SlackChannelService` — when inviting a mentioned user on a
  linked channel, post a message addressed to them with the mention text (the welcome).
- Builds on `slack-mention-invites`; same `slack-channel-lifecycle` capability. No schema change.
