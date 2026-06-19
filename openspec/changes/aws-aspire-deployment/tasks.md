## 1. AppHost AWS integration

- [x] 1.1 Add the `Aspire.Hosting.AWS` (13.x) package and enable `#pragma warning disable ASPIREAWSPUBLISHERS001`
- [x] 1.2 Configure the AWS target (region us-east-1, profile creds); AWS CDK CLI installed + `cdk bootstrap`
- [x] 1.3 De-risk: deployed the API to ECS Fargate via `aspire deploy` (CDK synth, ECR push, console exclusion all confirmed; stack `aws2` CREATE_COMPLETE)

## 2. Containerize the API (console excluded)

- [x] 2.1 API publishes as a container image (Aspire `PublishAsECSFargateServiceWithALB` via `AddAWSCDKEnvironment`)
- [x] 2.2 Console/TUI excluded from publish/deploy (added only under `ExecutionContext.IsRunMode`; 0 console resources in the template)
- [x] 2.3 Deployed image serves `/health` (Healthy) and `/`, `/workitems` over the ALB; webhook/admin endpoints present

## 3. Persistence (EFS-mounted SQLite)

- [ ] 3.1 Provision an EFS file system via CDK and mount it on the Fargate task
- [ ] 3.2 Point the API's `ConnectionStrings:JiraSync` at the EFS mount path (e.g., `Data Source=/data/jirasync.db`)
- [ ] 3.3 Verify data survives a restart/redeploy (single task enforced — concurrent writers would corrupt SQLite over EFS)

## 4. Secrets

- [ ] 4.1 Store Jira token, Slack bot token, and signing secret in AWS Secrets Manager
- [ ] 4.2 Inject the secrets into the API's configuration at runtime (no secrets in image/repo)

## 5. Public endpoint & deploy

- [x] 5.1 ECS Fargate behind an ALB with an ACM cert + 443 listener + Route 53 alias for **https://jsg.appcloud.systems** (via the `ConstructApplicationLoadBalancedFargateServiceCallback` CDK customization)
- [ ] 5.2 Pin the ECS service to a single instance (background-worker single-writer constraint)
- [x] 5.3 Ran `aspire deploy`; public URL: **https://jsg.appcloud.systems** (ALB DNS `aws2-Project-...elb.amazonaws.com`)
- [x] 5.4 Smoke tested `GET /health` over HTTPS → "Healthy" (valid ACM cert)

## Learnings (de-risk pass)

- [x] Container runs as non-root with a read-only working dir → SQLite needs a writable path (`/tmp` now; EFS `/data` next).
- [x] ALB target group health-checks `/` with a 200 matcher → the API root must return 200 (was a 302 redirect to `/swagger`).

## 6. Wire up & docs

- [ ] 6.1 Register the Jira webhook against the public URL and verify a live event round-trips
- [ ] 6.2 Document the deploy runbook (prereqs, `aspire deploy`, secrets, webhook registration) in the README
- [ ] 6.3 Note the Slack app Event Subscriptions + slash command will target this URL (handled in the Slack changes)
