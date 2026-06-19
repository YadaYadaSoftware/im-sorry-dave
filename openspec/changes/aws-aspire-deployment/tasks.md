## 1. AppHost AWS integration

- [ ] 1.1 Add the `Aspire.Hosting.AWS` package to the AppHost
- [ ] 1.2 Configure the AWS deployment target (region, credentials/profile) and an ECR repository
- [ ] 1.3 Validate a minimal `aspire deploy` end-to-end early to confirm the AWS path (de-risk maturity)

## 2. Containerize the API (console excluded)

- [ ] 2.1 Make the API publishable as a container image (Aspire-generated or Dockerfile)
- [ ] 2.2 Mark the console/TUI resource excluded from publish/deploy
- [ ] 2.3 Confirm the deployed image serves `/health` and the webhook/admin endpoints

## 3. Persistence

- [ ] 3.1 Decide the pairing: App Runner + RDS Postgres (Npgsql) or ECS Fargate + EFS + SQLite
- [ ] 3.2 (If RDS) Add the Npgsql EF Core provider, generate Postgres migrations, and remove SQLite-only query workarounds where the provider handles them
- [ ] 3.3 Provision the datastore (RDS instance or EFS volume) and wire its connection string from the secret store
- [ ] 3.4 Verify data survives a restart/redeploy

## 4. Secrets

- [ ] 4.1 Store Jira token, Slack bot token, and signing secret in AWS Secrets Manager
- [ ] 4.2 Inject the secrets into the API's configuration at runtime (no secrets in image/repo)

## 5. Public endpoint & deploy

- [ ] 5.1 Stand up the compute service with managed HTTPS (App Runner, or ECS + ALB + ACM)
- [ ] 5.2 Pin to a single instance (background-worker single-writer constraint)
- [ ] 5.3 Run `aspire deploy` with AWS credentials and capture the public base URL
- [ ] 5.4 Smoke test `GET /health` over HTTPS

## 6. Wire up & docs

- [ ] 6.1 Register the Jira webhook against the public URL and verify a live event round-trips
- [ ] 6.2 Document the deploy runbook (prereqs, `aspire deploy`, secrets, webhook registration) in the README
- [ ] 6.3 Note the Slack app Event Subscriptions + slash command will target this URL (handled in the Slack changes)
