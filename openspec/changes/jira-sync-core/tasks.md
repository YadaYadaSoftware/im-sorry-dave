## 1. Service foundation

- [ ] 1.1 Create the ASP.NET Core service project and solution structure
- [ ] 1.2 Add configuration + secret handling for Jira base URL, API token, and webhook secret
- [ ] 1.3 Set up persistence (work items, resource mappings, write-back outbox) with migrations
- [ ] 1.4 Define the canonical work-item domain model and repository

## 2. Jira client

- [ ] 2.1 Implement a Jira REST client (auth, issue fetch, JQL search, comment create/update)
- [ ] 2.2 Add rate-limit / `Retry-After` handling and bounded retries
- [ ] 2.3 Map Jira issue payloads to the canonical work-item model

## 3. Webhook ingestion

- [ ] 3.1 Add the HTTPS webhook endpoint for issue created/updated/deleted and comment created
- [ ] 3.2 Verify webhook secret/signature and reject unauthenticated requests with 401
- [ ] 3.3 Apply events to the internal store with stale/out-of-order guard via `updated` marker
- [ ] 3.4 Acknowledge within provider timeout; enqueue heavy work for async processing

## 4. Reconciliation

- [ ] 4.1 Implement a scheduled reconciliation sweep (JQL by `updated`) to refresh changed issues
- [ ] 4.2 Detect and soft-delete issues removed from Jira
- [ ] 4.3 Provide an initial backfill mode over target project(s)/filter

## 5. Mapping store

- [ ] 5.1 Implement the (resourceType, resourceId) ↔ workItemKey mapping with uniqueness on resourceId
- [ ] 5.2 Expose lookup-by-resource and lookup-by-work-item APIs for other capabilities
- [ ] 5.3 Enforce conflict rejection on duplicate resource links

## 6. Decision write-back

- [ ] 6.1 Define the structured managed-record format and stable record identity / marker
- [ ] 6.2 Implement write-back via outbox: post/update Jira comments and designated fields idempotently
- [ ] 6.3 Add attribution (source link + responsible person) to all written records
- [ ] 6.4 Implement retry with backoff for transient failures and operator surfacing for permanent failures

## 7. Validation

- [ ] 7.1 Unit tests for stale-event guard, idempotent write-back, and mapping uniqueness
- [ ] 7.2 Integration test against a Jira test project (webhook → store → write-back round trip)
- [ ] 7.3 Document configuration and webhook registration steps
