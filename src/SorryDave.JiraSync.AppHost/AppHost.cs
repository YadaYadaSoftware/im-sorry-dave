#pragma warning disable ASPIREAWSPUBLISHERS001 // experimental AWS publish/deploy APIs
using Aspire.Hosting.AWS.Deployment;

var builder = DistributedApplication.CreateBuilder(args);

// The API is the primary service; its /health endpoint gates dependents.
var api = builder.AddProject<Projects.SorryDave_JiraSync_Api>("api")
    .WithHttpHealthCheck("/health");

if (builder.ExecutionContext.IsRunMode)
{
    // Interactive smoke-test console — LOCAL RUN ONLY, never deployed (it's a Terminal.Gui TUI,
    // which can't run inside Aspire's process host). It opens the TUI in a new terminal window.
    var smokeTuiDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "SorryDave.JiraSync.SmokeTui"));

    builder.AddExecutable(
            "console",
            "powershell",
            smokeTuiDir,
            "-NoProfile", "-Command", "Start-Process cmd -ArgumentList '/k','dotnet run --no-build'")
        .WithReference(api)
        .WaitFor(api)
        .WithExplicitStart();
}
else
{
    // Publish/deploy (e.g. `aspire deploy`): a CDK compute environment (VPC + ECS cluster) plus
    // the API → AWS ECS Fargate behind an Application Load Balancer. EFS-mounted SQLite, the ACM
    // cert for jsg.appcloud.systems, and single-instance are layered on next once the minimal
    // ECS+ALB path is validated.
    builder.AddAWSCDKEnvironment("aws2", CDKDefaultsProviderFactory.Preview_V1);
    // SQLite lives on an EFS-mounted /data (durable across task restarts/redeploys). The EFS file
    // system, access point, volume, and mount are wired in the construct callback below.
    api.WithEnvironment("ConnectionStrings__JiraSync", "Data Source=/data/jirasync.db")
       // Non-secret Jira config (the API token comes from Secrets Manager below). With these set,
       // the deployed API runs in REAL mode and mirrors the MDP project.
       .WithEnvironment("Jira__BaseUrl", "https://tim-bassett.atlassian.net/")
       .WithEnvironment("Jira__Email", "hounddog@gmail.com")
       .WithEnvironment("Jira__ProjectKeys__0", "MDP")
       // Webhook shared secret (anti-spoofing) as a plain env var supplied at deploy time via the
       // WebhookSecret config key (e.g. $env:WebhookSecret) — not stored in the repo. Empty = the
       // endpoint accepts unsigned requests.
       .WithEnvironment("Webhook__Secret", builder.Configuration["WebhookSecret"] ?? "")
       .PublishAsECSFargateServiceWithALB(new PublishECSFargateServiceWithALBConfig
       {
           // Inject the Jira API token from AWS Secrets Manager as a container secret (never in the
           // image or repo). ECS auto-grants the task execution role read access to referenced secrets.
           PropsApplicationLoadBalancedTaskImageOptionsCallback = (ctx, props) =>
           {
               var stack = (Amazon.CDK.Stack)ctx.GetType()
                   .GetField("_stack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                   .GetValue(ctx)!;
               var jiraToken = Amazon.CDK.AWS.SecretsManager.Secret.FromSecretNameV2(stack, "JiraApiToken", "jira-sync/jira-api-token");
               props.Secrets ??= new Dictionary<string, Amazon.CDK.AWS.ECS.Secret>();
               props.Secrets["Jira__ApiToken"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(jiraToken);
           },

           // Single instance with non-overlapping deploys: EFS-mounted SQLite is single-writer, so a
           // rolling deploy must stop the old task before starting the new one (two concurrent
           // writers would corrupt the DB over NFS). Brief downtime on deploy is acceptable here.
           PropsApplicationLoadBalancedFargateServiceCallback = (ctx, props) =>
           {
               props.DesiredCount = 1;
               props.MinHealthyPercent = 0;
               props.MaxHealthyPercent = 100;
           },

           // Add public HTTPS on jsg.appcloud.systems: an ACM cert (DNS-validated via the Route 53
           // zone for appcloud.systems), a 443 listener on the ALB, and an alias A-record.
           ConstructApplicationLoadBalancedFargateServiceCallback = (ctx, svc) =>
           {
               var stack = Amazon.CDK.Stack.Of(svc);

               // ECS enables Availability Zone Rebalancing by default, which forbids MaximumPercent
               // <= 100. Disable it so the single-instance stop-then-start deployment is permitted.
               ((Amazon.CDK.CfnResource)svc.Service.Node.DefaultChild!)
                   .AddPropertyOverride("AvailabilityZoneRebalancing", "DISABLED");

               var zone = Amazon.CDK.AWS.Route53.HostedZone.FromHostedZoneAttributes(stack, "DaveZone",
                   new Amazon.CDK.AWS.Route53.HostedZoneAttributes
                   {
                       HostedZoneId = "Z06422172SASV44F5Y8VA",
                       ZoneName = "appcloud.systems"
                   });

               var cert = new Amazon.CDK.AWS.CertificateManager.Certificate(stack, "DaveCert",
                   new Amazon.CDK.AWS.CertificateManager.CertificateProps
                   {
                       DomainName = "jsg.appcloud.systems",
                       Validation = Amazon.CDK.AWS.CertificateManager.CertificateValidation.FromDns(zone)
                   });

               svc.LoadBalancer.AddListener("Https", new Amazon.CDK.AWS.ElasticLoadBalancingV2.BaseApplicationListenerProps
               {
                   Port = 443,
                   Protocol = Amazon.CDK.AWS.ElasticLoadBalancingV2.ApplicationProtocol.HTTPS,
                   Certificates = new[] { Amazon.CDK.AWS.ElasticLoadBalancingV2.ListenerCertificate.FromCertificateManager(cert) },
                   DefaultTargetGroups = new[] { svc.TargetGroup }
               });

               _ = new Amazon.CDK.AWS.Route53.ARecord(stack, "DaveAlias", new Amazon.CDK.AWS.Route53.ARecordProps
               {
                   Zone = zone,
                   RecordName = "jsg",
                   Target = Amazon.CDK.AWS.Route53.RecordTarget.FromAlias(
                       new Amazon.CDK.AWS.Route53.Targets.LoadBalancerTarget(svc.LoadBalancer))
               });

               // EFS-mounted SQLite at /data (durable). The access point forces uid/gid 1000 and owns
               // /data, so the container writes regardless of its own user. NFS (2049) is opened from
               // the service security group. RemovalPolicy.DESTROY cleans it up on `cdk destroy`.
               var efs = new Amazon.CDK.AWS.EFS.FileSystem(stack, "DaveEfs", new Amazon.CDK.AWS.EFS.FileSystemProps
               {
                   Vpc = svc.Cluster.Vpc,
                   Encrypted = true,
                   RemovalPolicy = Amazon.CDK.RemovalPolicy.DESTROY
               });
               var accessPoint = efs.AddAccessPoint("DaveAp", new Amazon.CDK.AWS.EFS.AccessPointOptions
               {
                   Path = "/data",
                   CreateAcl = new Amazon.CDK.AWS.EFS.Acl { OwnerUid = "1000", OwnerGid = "1000", Permissions = "0755" },
                   PosixUser = new Amazon.CDK.AWS.EFS.PosixUser { Uid = "1000", Gid = "1000" }
               });
               efs.Connections.AllowDefaultPortFrom(svc.Service);

               svc.TaskDefinition.AddVolume(new Amazon.CDK.AWS.ECS.Volume
               {
                   Name = "data",
                   EfsVolumeConfiguration = new Amazon.CDK.AWS.ECS.EfsVolumeConfiguration
                   {
                       FileSystemId = efs.FileSystemId,
                       TransitEncryption = "ENABLED",
                       AuthorizationConfig = new Amazon.CDK.AWS.ECS.AuthorizationConfig
                       {
                           AccessPointId = accessPoint.AccessPointId,
                           Iam = "DISABLED"
                       }
                   }
               });
               svc.TaskDefinition.DefaultContainer!.AddMountPoints(new Amazon.CDK.AWS.ECS.MountPoint
               {
                   ContainerPath = "/data",
                   SourceVolume = "data",
                   ReadOnly = false
               });
           }
       });
}

builder.Build().Run();
