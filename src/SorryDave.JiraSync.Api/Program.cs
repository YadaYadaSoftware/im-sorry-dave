using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SorryDave.JiraSync.Api.Endpoints;
using SorryDave.JiraSync.Core.DependencyInjection;
using SorryDave.JiraSync.Core.Persistence;

var builder = WebApplication.CreateBuilder(args);

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

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapWebhookEndpoints();
app.MapWorkItemEndpoints();

app.Run();

// Exposed so WebApplicationFactory-based tests can reference the entry point.
public partial class Program { }
