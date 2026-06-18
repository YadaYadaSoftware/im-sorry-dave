using System.Text.Json;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Sync;

namespace SorryDave.JiraSync.Api.Endpoints;

public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/jira", async (
            HttpRequest request,
            WebhookProcessor processor,
            IOptions<WebhookOptions> webhookOptions,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("JiraWebhook");

            if (!IsSecretValid(request, webhookOptions.Value))
            {
                log.LogWarning("Rejected Jira webhook with missing/invalid secret.");
                return Results.Json(new { error = "invalid or missing webhook secret" }, statusCode: 401);
            }

            JsonDocument doc;
            try
            {
                doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new { error = "invalid JSON", detail = ex.Message });
            }

            using (doc)
            {
                var result = await processor.ProcessAsync(doc.RootElement, ct);
                return Results.Ok(result);
            }
        })
        .WithName("JiraWebhook")
        .WithSummary("Receive Jira webhook events (issue created/updated/deleted, comment created).");
    }

    private static bool IsSecretValid(HttpRequest request, WebhookOptions options)
    {
        // No secret configured → checks are skipped (local-review convenience). Configure a
        // secret in any shared environment.
        if (string.IsNullOrWhiteSpace(options.Secret))
            return true;

        var provided = request.Headers["X-Webhook-Secret"].FirstOrDefault()
                       ?? request.Query["secret"].FirstOrDefault();

        return !string.IsNullOrEmpty(provided) &&
               CryptographicEquals(provided, options.Secret);
    }

    private static bool CryptographicEquals(string a, string b)
    {
        var ba = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
