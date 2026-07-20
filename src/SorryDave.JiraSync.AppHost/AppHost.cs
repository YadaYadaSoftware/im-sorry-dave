#pragma warning disable ASPIREAWSPUBLISHERS001 // experimental AWS publish/deploy APIs
using Aspire.Hosting.AWS.Deployment;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// Load the AppHost's own user-secrets so secrets set locally (e.g. Slack:BotToken / Slack:SigningSecret)
// can be transported into AWS SSM at deploy time, regardless of host environment.
builder.Configuration.AddUserSecrets(System.Reflection.Assembly.GetExecutingAssembly(), optional: true);

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
    // Transport secrets from local config (the AppHost's user-secrets) into SSM Parameter Store, so
    // a single `aspire deploy` provisions them as SecureString. Written via the SSM API — NOT
    // CloudFormation, which can't create SecureString — so the values never land in a template.
    // Idempotent upsert; only keys present locally are written (so a missing bot token is skipped).
    var transportable = new (string Key, string Param)[]
    {
        ("Slack:BotToken", "/jira-sync/Slack/BotToken"),
        ("Slack:SigningSecret", "/jira-sync/Slack/SigningSecret"),
        ("Anthropic:ApiKey", "/jira-sync/Anthropic/ApiKey"),
    };
    var toTransport = transportable.Where(p => !string.IsNullOrWhiteSpace(builder.Configuration[p.Key])).ToArray();
    if (toTransport.Length > 0)
    {
        using var ssm = new Amazon.SimpleSystemsManagement.AmazonSimpleSystemsManagementClient(Amazon.RegionEndpoint.USEast1);
        foreach (var (key, param) in toTransport)
        {
            await ssm.PutParameterAsync(new Amazon.SimpleSystemsManagement.Model.PutParameterRequest
            {
                Name = param,
                Value = builder.Configuration[key],
                Type = Amazon.SimpleSystemsManagement.ParameterType.SecureString,
                Overwrite = true,
            });
        }
        Console.WriteLine($"Transported {toTransport.Length} secret(s) to SSM /jira-sync/Slack/*.");
    }

    // Publish/deploy (e.g. `aspire deploy`): a CDK compute environment (VPC + ECS cluster) plus
    // the API → AWS ECS Fargate behind an Application Load Balancer. EFS-mounted SQLite, the ACM
    // cert for jsg.appcloud.systems, and single-instance are layered on next once the minimal
    // ECS+ALB path is validated.
    builder.AddAWSCDKEnvironment("aws2", CDKDefaultsProviderFactory.Preview_V1);
    // SQLite lives on an EFS-mounted /data (durable across task restarts/redeploys). The EFS file
    // system, access point, volume, and mount are wired in the construct callback below.
    api.WithEnvironment("ConnectionStrings__JiraSync", "Data Source=/data/jirasync.db")
       // Non-secret Jira config; secrets (Jira token, webhook secret, future Slack/Anthropic keys)
       // are resolved at runtime from SSM Parameter Store — see Aws__ParameterStorePath below.
       .WithEnvironment("Jira__BaseUrl", "https://elevate-digital.atlassian.net/")
       .WithEnvironment("Jira__Email", "tim-bassett@elevate-digital.com")
       .WithEnvironment("Jira__ProjectKeys__0", "SPMCLOUD")
       // Enable the API's SSM Parameter Store provider over the /jira-sync prefix
       // (/jira-sync/Jira/ApiToken -> Jira:ApiToken, /jira-sync/Webhook/Secret -> Webhook:Secret).
       // The task role is granted read on this prefix in the construct callback below.
       .WithEnvironment("Aws__ParameterStorePath", "/jira-sync")
       .PublishAsECSFargateServiceWithALB(new PublishECSFargateServiceWithALBConfig
       {
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

               // The task role reads secrets from SSM Parameter Store under /jira-sync/* at startup.
               // ONE grant covers all current and future parameters under the prefix, so adding a
               // secret never needs a new grant or redeploy (plus kms:Decrypt for SecureString).
               svc.TaskDefinition.TaskRole.AddToPrincipalPolicy(new Amazon.CDK.AWS.IAM.PolicyStatement(
                   new Amazon.CDK.AWS.IAM.PolicyStatementProps
                   {
                       // GetParametersByPath authorizes against the path NODE (parameter/jira-sync),
                       // GetParameter(s) against the child parameters — grant both.
                       Actions = new[] { "ssm:GetParametersByPath", "ssm:GetParameters", "ssm:GetParameter" },
                       Resources = new[]
                       {
                           $"arn:aws:ssm:{stack.Region}:{stack.Account}:parameter/jira-sync",
                           $"arn:aws:ssm:{stack.Region}:{stack.Account}:parameter/jira-sync/*"
                       }
                   }));
               svc.TaskDefinition.TaskRole.AddToPrincipalPolicy(new Amazon.CDK.AWS.IAM.PolicyStatement(
                   new Amazon.CDK.AWS.IAM.PolicyStatementProps
                   {
                       Actions = new[] { "kms:Decrypt" },
                       Resources = new[] { "*" },
                       Conditions = new Dictionary<string, object>
                       {
                           ["StringEquals"] = new Dictionary<string, object>
                           {
                               ["kms:ViaService"] = $"ssm.{stack.Region}.amazonaws.com"
                           }
                       }
                   }));

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
