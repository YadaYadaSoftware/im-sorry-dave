using System.Net;
using Microsoft.Extensions.Logging;

namespace SorryDave.JiraSync.Core.Jira;

/// <summary>
/// Delegating handler that retries transient Jira responses (429/5xx) with backoff,
/// honouring the <c>Retry-After</c> header when present. Permanent responses pass through.
/// </summary>
public class JiraRateLimitHandler : DelegatingHandler
{
    private readonly ILogger<JiraRateLimitHandler> _logger;
    private const int MaxRetries = 4;

    public JiraRateLimitHandler(ILogger<JiraRateLimitHandler> logger) => _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 0; ; attempt++)
        {
            response?.Dispose();
            response = await base.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode || attempt >= MaxRetries ||
                !JiraApiException.IsTransientStatus(response.StatusCode))
                return response;

            var delay = GetRetryDelay(response, attempt);
            _logger.LogWarning(
                "Jira returned {Status}; retry {Attempt}/{Max} after {Delay}.",
                (int)response.StatusCode, attempt + 1, MaxRetries, delay);
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter is { } ra)
        {
            if (ra.Delta is { } delta) return delta;
            if (ra.Date is { } date)
            {
                var until = date - DateTimeOffset.UtcNow;
                if (until > TimeSpan.Zero) return until;
            }
        }
        // Exponential backoff: 1s, 2s, 4s, 8s.
        return TimeSpan.FromSeconds(Math.Pow(2, attempt));
    }
}
