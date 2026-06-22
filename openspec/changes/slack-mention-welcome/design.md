## Context

`slack-mention-invites` invites users @mentioned in a comment or the description to the work item's
channel (resolving accountIds, idempotent). It captures only the **accountIds**, not the surrounding
text. This change adds the mention's **text** so an invited person gets a welcome message explaining
why they're there. The pieces already exist: `WebhookProcessor` fetches the comment ADF (for
accountIds) and `AdfText.Flatten` turns ADF into readable text — so capturing the text is cheap.

## Goals / Non-Goals

**Goals:**
- A mention-driven invite posts a channel message **to the invitee** with the **text of the mention**.
- That message serves as the **welcome** when no welcome message exists for the invite (comment /
  description-edit on an existing channel).

**Non-Goals:**
- DMing the invitee (channels are the surface; the message @-addresses them in-channel).
- Re-posting at auto-provision where the description welcome already carries the text.
- Changing who gets invited (that's `slack-mention-invites`).

## Decisions

### Carry the mention text on the change event
**Decision:** Add `MentionContext : string?` to `WorkItemChange` — the readable text where the mention
occurred. Producers:
- **Comment:** `WebhookProcessor` already fetches the comment ADF; flatten it (`AdfText.Flatten`) and
  set `MentionContext` to the comment text. (Extend `GetCommentMentionsAsync` to also return the text,
  or add a sibling that returns `(accountIds, text)`.)
- **Description edit:** set `MentionContext` to the work item's description (already on `WorkItem`).

*Why one context string for the change:* a comment/edit is a single coherent block; all users
mentioned in it share the same context. Keeps the model simple. *Alternative — per-mention snippet:*
rejected (ADF doesn't cleanly delimit per-mention text; the whole block is the useful context).

### Post the welcome to each invited mentionee
**Decision:** In `SlackChannelService`, when a non-created change invites mentioned users on a linked
channel, after a successful invite post one message addressed to them, e.g.
`👋 <@U…> you were mentioned on MDP-9: "<context>"`. Group the just-invited users into a single
message (`<@U1> <@U2> …`) to avoid spam. Truncate the context defensively. Best-effort (a failed post
never blocks the invite). *Why in-channel + @mention:* it's visible to the person (Slack notifies on
@mention) and to the channel, matching "welcome message to the invitee."

### It is the welcome when there isn't one
**Decision:** At **auto-provision**, description mentions are invited while the welcome (header +
description) is posted — the description message already contains the mention text, so **no extra**
message is posted there. For **comment / description-edit** invites on an existing channel, there is
no welcome in that moment, so this mention-context message **is** the welcome. Implement by posting
the welcome from the mention-invite path (the non-created change), not from `SeedContextAsync`.

### Only post for users actually invited now
**Decision:** Post the welcome only for users who resolved and were invited on this event (not
already-known/un-resolvable ones), so re-mentioning someone already in the channel doesn't re-welcome
them on every comment. *Note:* `InviteAsync` is idempotent and doesn't report "was already a member";
to avoid re-welcoming, track which mentionees are newly added — simplest is to welcome the resolved
set for this event and accept occasional re-welcome, or check membership first. Prefer welcoming the
resolved set for this event (simple); revisit if it proves noisy.

## Risks / Trade-offs

- [Re-welcome noise on repeated mentions] → welcome the per-event resolved set; if noisy, add a
  membership check before welcoming. Accept simple behavior first.
- [Long comment text] → truncate the context.
- [Context text leakage] → it's the same text already in Jira/visible to channel members; no new
  exposure.

## Migration Plan

1. Capture comment text (flatten ADF) + description text into `WorkItemChange.MentionContext`.
2. `SlackChannelService`: on a mention-invite (non-created, linked channel), post the welcome to the
   invited mentionees with the context.
3. Tests: comment mention → invitee welcomed with the comment text; description-edit mention →
   welcomed with the description; no double-welcome at auto-provision.
4. Deploy; add a comment mentioning a (non-member) user → they're invited and welcomed with the text.

## Open Questions

- Welcome per-event resolved set vs. only truly-new members — start with per-event set; tighten if
  noisy.
