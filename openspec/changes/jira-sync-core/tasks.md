## 1. Service foundation

- [x] 1.1 Create the ASP.NET Core service project and solution structure
- [x] 1.2 Add configuration + secret handling for Jira base URL, API token, and webhook secret
- [x] 1.3 Set up persistence (work items, resource mappings, write-back outbox) with migrations
- [x] 1.4 Define the canonical work-item domain model and repository

## 2. Jira client

- [x] 2.1 Implement a Jira REST client (auth, issue fetch, JQL search, comment create/update)
- [x] 2.2 Add rate-limit / `Retry-After` handling and bounded retries
- [x] 2.3 Map Jira issue payloads to the canonical work-item model

## 3. Webhook ingestion

- [x] 3.1 Add the HTTPS webhook endpoint for issue created/updated/deleted and comment created
- [x] 3.2 Verify webhook secret/signature and reject unauthenticated requests with 401
- [x] 3.3 Apply events to the internal store with stale/out-of-order guard via `updated` marker
- [x] 3.4 Acknowledge within provider timeout; enqueue heavy work for async processing

## 4. Reconciliation

- [x] 4.1 Implement a scheduled reconciliation sweep (JQL by `updated`) to refresh changed issues
- [x] 4.2 Detect and soft-delete issues removed from Jira
- [x] 4.3 Provide an initial backfill mode over target project(s)/filter

## 5. Mapping store

- [x] 5.1 Implement the (resourceType, resourceId) ↔ workItemKey mapping with uniqueness on resourceId
- [x] 5.2 Expose lookup-by-resource and lookup-by-work-item APIs for other capabilities
- [x] 5.3 Enforce conflict rejection on duplicate resource links

## 6. Decision write-back

- [x] 6.1 Define the structured managed-record format and stable record identity / marker
- [x] 6.2 Implement write-back via outbox: post/update Jira comments and designated fields idempotently
- [x] 6.3 Add attribution (source link + responsible person) to all written records
- [x] 6.4 Implement retry with backoff for transient failures and operator surfacing for permanent failures

## 7. Validation

- [x] 7.1 Unit tests for stale-event guard, idempotent write-back, and mapping uniqueness
- [x] 7.2 Integration test against a Jira test project (webhook → store → write-back round trip) — credential-gated `JiraRoundTripIntegrationTests`; verified green against real Jira Cloud (issue MDP-7)
- [x] 7.3 Document configuration and webhook registration steps

## 8. Operations & verification

- [x] 8.1 Support layered configuration (appsettings + environment variables + user-secrets) with `__`-nested env keys
- [x] 8.2 Add a devops configuration/deployment README for the API (`src/SorryDave.JiraSync.Api/README.md`)
- [x] 8.3 Credential-gated round-trip integration test reading Jira settings from user-secrets/env; runs when credentials are present, skips otherwise
- [x] 8.4 Integration test leaves its decision comment on the test issue for inspection (each run uses a unique record identity)
