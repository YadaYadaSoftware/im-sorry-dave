## 1. AppHost AWS integration

- [x] 1.1 Add the `Aspire.Hosting.AWS` (13.x) package and enable `#pragma warning disable ASPIREAWSPUBLISHERS001`
- [x] 1.2 Configure the AWS target (region us-east-1, profile creds); AWS CDK CLI installed + `cdk bootstrap`
- [x] 1.3 De-risk: deployed the API to ECS Fargate via `aspire deploy` (CDK synth, ECR push, console exclusion all confirmed; stack `aws2` CREATE_COMPLETE)

## 2. Containerize the API (console excluded)

- [x] 2.1 API publishes as a container image (Aspire `PublishAsECSFargateServiceWithALB` via `AddAWSCDKEnvironment`)
- [x] 2.2 Console/TUI excluded from publish/deploy (added only under `ExecutionContext.IsRunMode`; 0 console resources in the template)
- [x] 2.3 Deployed image serves `/health` (Healthy) and `/`, `/workitems` over the ALB; webhook/admin endpoints present

## 3. Persistence (EFS-mounted SQLite)

- [x] 3.1 Provisioned EFS (file system + access point posixUser 1000 + 2 mount targets) via CDK and mounted at `/data` on the Fargate task (`fs-04eafafe383ef31a2`)
- [x] 3.2 `ConnectionStrings__JiraSync = Data Source=/data/jirasync.db` (app opens/writes SQLite on EFS — confirmed by healthy startup)
- [x] 3.3 Durable across restarts via EFS; single task enforced with **non-overlapping** deploys (desiredCount=1, max=100/min=0, AZ-rebalancing disabled) so concurrent writers can't corrupt SQLite over NFS

## 4. Secrets

- [x] 4.1 Stored the Jira token in AWS Secrets Manager (`jira-sync/jira-api-token`); Slack secrets to follow with the Slack changes
- [x] 4.2 Injected the token as an ECS container secret (`Jira__ApiToken` via `Secret.FromSecretsManager`); non-secret Jira config as env vars. Deployed API verified in REAL mode mirroring MDP-1..7

## 5. Public endpoint & deploy

- [x] 5.1 ECS Fargate behind an ALB with an ACM cert + 443 listener + Route 53 alias for **https://jsg.appcloud.systems** (via the `ConstructApplicationLoadBalancedFargateServiceCallback` CDK customization)
- [x] 5.2 Pinned to a single instance (desiredCount=1) with non-overlapping (stop-then-start) deploys; AZ rebalancing disabled to allow MaximumPercent=100
- [x] 5.3 Ran `aspire deploy`; public URL: **https://jsg.appcloud.systems** (ALB DNS `aws2-Project-...elb.amazonaws.com`)
- [x] 5.4 Smoke tested `GET /health` over HTTPS → "Healthy" (valid ACM cert)

## Learnings (de-risk pass)

- [x] Container runs as non-root with a read-only working dir → SQLite needs a writable path (`/tmp` now; EFS `/data` next).
- [x] ALB target group health-checks `/` with a 200 matcher → the API root must return 200 (was a 302 redirect to `/swagger`).

## 6. Wire up & docs

- [ ] 6.1 Register the Jira webhook against the public URL and verify a live event round-trips
- [ ] 6.2 Document the deploy runbook (prereqs, `aspire deploy`, secrets, webhook registration) in the README
- [ ] 6.3 Note the Slack app Event Subscriptions + slash command will target this URL (handled in the Slack changes)
