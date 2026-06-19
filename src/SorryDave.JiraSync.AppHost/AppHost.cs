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
    // The container runs as a non-root user with a read-only working dir, so SQLite must live in a
    // writable path. /tmp is writable but ephemeral — fine for this validation pass; EFS-mounted
    // /data for durable persistence is the next iteration.
    api.WithEnvironment("ConnectionStrings__JiraSync", "Data Source=/tmp/jirasync.db")
       .PublishAsECSFargateServiceWithALB(new PublishECSFargateServiceWithALBConfig
       {
           // Add public HTTPS on jsg.appcloud.systems: an ACM cert (DNS-validated via the Route 53
           // zone for appcloud.systems), a 443 listener on the ALB, and an alias A-record.
           ConstructApplicationLoadBalancedFargateServiceCallback = (ctx, svc) =>
           {
               var stack = Amazon.CDK.Stack.Of(svc);

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
           }
       });
}

builder.Build().Run();
