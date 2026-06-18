using System.Net;

namespace SorryDave.JiraSync.Core.Jira;

/// <summary>
/// Raised by the Jira client on a failed call. <see cref="IsTransient"/> distinguishes
/// retryable failures (timeouts, 5xx, 429) from permanent ones (404, 403, 400).
/// </summary>
public class JiraApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public bool IsTransient { get; }

    public JiraApiException(string message, HttpStatusCode? statusCode, bool isTransient, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        IsTransient = isTransient;
    }

    public static bool IsTransientStatus(HttpStatusCode code) =>
        code == HttpStatusCode.RequestTimeout ||
        code == HttpStatusCode.TooManyRequests ||
        (int)code >= 500;
}
