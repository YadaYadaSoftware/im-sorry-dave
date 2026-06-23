# Tasks

## 1. Jira create-issue capability

- [ ] 1.1 Add `CreateIssueAsync(projectKey, issueType, summary, description)` returning the new issue
      key to `IJiraClient` (net-new; client currently only get/search/add+edit comment)
- [ ] 1.2 Implement `CreateIssueAsync` against the Jira create-issue API
- [ ] 1.3 Add config (SSM): default project key and issue-type mapping for created issues

## 2. Extract candidate action items

- [ ] 2.1 Extend/reuse `IDecisionExtractor` to emit candidate action items (title, description, issue
      type), gated on `Anthropic:ApiKey` with the fake fallback
- [ ] 2.2 Bound/truncate the conversation or pasted text passed to the extractor

## 3. Trigger command

- [ ] 3.1 Handle the `/triage` (`/actions`) slash command; extract over recent `CapturedMessage` for
      the channel, or over pasted text when supplied
- [ ] 3.2 Resolve the channel → work item link via `IMappingStore` for back-linking

## 4. Confirm via Block Kit

- [ ] 4.1 Render one candidate card per action item (title, description, issue type) with
      Confirm/Reject, reusing the existing Block Kit pattern
- [ ] 4.2 Route Confirm/Reject at `/slack/interactivity`; embed the candidate payload + dedupe key in
      the action value
- [ ] 4.3 On reject, create nothing

## 5. Create on confirm (idempotent, linked, attributed)

- [ ] 5.1 On confirm, create the issue via `IWriteBackService` / outbox using a deterministic dedupe
      key derived from the candidate; re-confirm returns the existing issue
- [ ] 5.2 Resolve project + issue type from config; back-link to the originating work item via
      `IMappingStore` when present
- [ ] 5.3 Attribute creation to the confirming Slack user and record the Slack channel as the source
- [ ] 5.4 Post a channel confirmation naming the new issue key

## 6. Tests

- [ ] 6.1 Extraction over conversation → one candidate card per action item
- [ ] 6.2 Confirm → exactly one Jira issue created with title/description/type
- [ ] 6.3 Re-confirm the same candidate → no second issue; existing key reported
- [ ] 6.4 Reject → no issue created
- [ ] 6.5 Channel linked to a work item → created issue back-links; unlinked channel → created in
      default project with no back-link
- [ ] 6.6 No API key → fake fallback; no project configured → no create, reported

## 7. Deploy & verify

- [ ] 7.1 Deploy; run `/triage` in a channel, confirm a candidate, verify a new Jira issue exists and
      links back
- [ ] 7.2 Docs: note the conversation-to-work-items command in the README Slack section
