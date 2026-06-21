using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SorryDave.JiraSync.Api.Endpoints;
using SorryDave.JiraSync.Core.DependencyInjection;
using SorryDave.JiraSync.Core.Persistence;
using SorryDave.JiraSync.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// In a deployed environment, load secrets from AWS SSM Parameter Store under a path prefix
// (e.g. /jira-sync). Parameter paths map to config keys: /jira-sync/Jira/ApiToken -> Jira:ApiToken.
// Enabled only when Aws:ParameterStorePath is set (the AppHost sets it for AWS). Required (optional:
// false) so an unreachable store fails startup rather than running uncredentialed. Added last, so
// the store is authoritative for the keys it supplies.
var ssmPath = builder.Configuration["Aws:ParameterStorePath"];
if (!string.IsNullOrWhiteSpace(ssmPath))
{
    builder.Configuration.AddSystemsManager(ssmPath, optional: false);

    // Fail fast: in a deployed environment we expect real Jira. Never silently fall back to the
    // fake backend because a required secret didn't resolve. (Names the missing keys, not values.)
    if (builder.Configuration.GetValue<bool?>("Jira:UseFake") != true)
    {
        var missing = new[] { "Jira:BaseUrl", "Jira:Email", "Jira:ApiToken" }
            .Where(k => string.IsNullOrWhiteSpace(builder.Configuration[k]))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Required secrets not resolved at startup: {string.Join(", ", missing)} " +
                $"(expected from SSM Parameter Store under '{ssmPath}'). Refusing to start.");
    }
}

builder.AddServiceDefaults();
builder.Services.AddJiraSyncCore(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Accept and emit enums as strings (e.g. "Decision") in request/response JSON.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// Apply migrations on startup so the SQLite file is ready for review with no manual steps.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<JiraSyncDbContext>();
    db.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();

app.MapDefaultEndpoints(); // /health and /alive (used by Aspire health-gated startup)
// Root returns 200 (load-balancer health checks hit "/"); Swagger UI is at /swagger.
app.MapGet("/", () => Results.Ok(new { service = "jira-sync-core", status = "ok", swagger = "/swagger" }));

app.MapWebhookEndpoints();
app.MapWorkItemEndpoints();
app.MapAdminEndpoints();
app.MapSlackEndpoints();

app.Run();

// Exposed so WebApplicationFactory-based tests can reference the entry point.
public partial class Program { }
