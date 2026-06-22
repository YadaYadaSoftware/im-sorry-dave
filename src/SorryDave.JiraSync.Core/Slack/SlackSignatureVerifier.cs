using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SorryDave.JiraSync.Core.Slack;

/// <summary>
/// Verifies inbound Slack request signatures (Events API, slash commands, interactivity) per Slack's
/// scheme: <c>v0=HMAC_SHA256(signing_secret, "v0:{timestamp}:{raw_body}")</c>, with a timestamp
/// freshness check to reject replays.
/// </summary>
public static class SlackSignatureVerifier
{
    public static bool IsValid(
        string? signingSecret, string? signature, string? timestamp, string rawBody,
        DateTimeOffset now, TimeSpan? tolerance = null)
    {
        if (string.IsNullOrEmpty(signingSecret) || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp))
            return false;

        // Replay guard: reject stale timestamps (default ±5 minutes).
        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts))
            return false;
        var skew = now - DateTimeOffset.FromUnixTimeSeconds(ts);
        if (skew.Duration() > (tolerance ?? TimeSpan.FromMinutes(5)))
            return false;

        var basestring = $"v0:{timestamp}:{rawBody}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(basestring));
        var expected = "v0=" + Convert.ToHexString(hash).ToLowerInvariant();

        // Constant-time compare.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature));
    }
}
