# Tasks

## 1. Claude blocker classification

- [ ] 1.1 Extend `IDecisionExtractor` (or add a sibling) to classify a bounded conversation window as a
      blocker/risk, returning a confidence score and supporting `CapturedMessage` id(s) as evidence
- [ ] 1.2 Reuse the `Anthropic:ApiKey` gate and fake fallback so no spurious detections are raised
      without a key
- [ ] 1.3 Add the configurable confidence threshold and surface only detections at or above it
- [ ] 1.4 Run classification on the capture/summarization extraction pass over a bounded, debounced
      window (no whole-channel sweep)

## 2. Detection store and de-duplication

- [ ] 2.1 Persist raised/dismissed detections keyed by work item with a signature (in `IMappingStore`
      or a sibling detection store)
- [ ] 2.2 Suppress a new detection whose signature matches an already-raised or dismissed one within
      the configurable dedup window
- [ ] 2.3 Allow a distinct second blocker (different signature) to raise and notify

## 3. In-channel notification

- [ ] 3.1 Resolve the work item's linked channel via `IMappingStore`; no channel → no action
- [ ] 3.2 Resolve assignee/reporter accountIds (mirrored `WorkItem`) to Slack ids via
      `IJiraSlackIdentityResolver`; skip unresolvable identities
- [ ] 3.3 Post the @-mention notification via `ISlackClient`, best-effort (a failed post never crashes
      capture)

## 4. Optional Jira annotation

- [ ] 4.1 Behind config (off by default), add a managed `blocked` label and/or managed comment via
      `IWriteBackService`

## 5. Human dismissal

- [ ] 5.1 Include a dismiss action on the notification; handle it under the `/slack` endpoints
- [ ] 5.2 Record the dismissed signature so de-duplication suppresses matching detections

## 6. Tests

- [ ] 6.1 Blocker signal above threshold → detection raised with evidence
- [ ] 6.2 Signal below threshold / fake extractor → no detection
- [ ] 6.3 Detection → assignee and reporter @-mentioned in channel; unresolvable identity skipped
- [ ] 6.4 No linked channel → no notification
- [ ] 6.5 Repeated same-signature blocker → no duplicate notification; distinct blocker → new
      notification
- [ ] 6.6 Dismiss action → detection recorded dismissed and not re-raised
- [ ] 6.7 Jira annotation enabled → `blocked` label/comment written via `IWriteBackService`; disabled →
      no Jira write

## 7. Deploy & verify

- [ ] 7.1 Pilot on one channel; tune the confidence threshold; confirm a real "we're blocked by…"
      message notifies the assignee/reporter
- [ ] 7.2 Docs: note blocker detection (threshold, dismissal, optional Jira annotation) in the README
