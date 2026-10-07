using Microsoft.AspNetCore.Http;

namespace Infrastructure.Logging;

/// <summary>
/// Things that must never reach the logs, whatever <see cref="RequestLoggingOptions"/> says:
/// credentials in request headers and the one-time response that carries a new sensor key.
/// </summary>
public static class SensitiveData
{
    private static readonly string[] SensitiveHeaders = ["Authorization", "X-API-Key"];

    /// <summary>All request headers, with the credential headers replaced by <paramref name="mask"/>.</summary>
    public static Dictionary<string, object?> RedactHeaders(IHeaderDictionary headers, string mask) =>
        headers.ToDictionary(
            h => h.Key,
            h => (object?)(SensitiveHeaders.Contains(h.Key, StringComparer.OrdinalIgnoreCase)
                ? mask
                : h.Value.ToString()));

    /// <summary>
    /// True for the two requests whose response body contains a full sensor key:
    /// POST /api/admin/locations and POST /api/admin/locations/{id}/api-key/rotate.
    /// </summary>
    public static bool RevealsApiKey(string method, PathString path)
    {
        if (!HttpMethods.IsPost(method) || !path.HasValue)
        {
            return false;
        }

        string[] segments = path.Value!.Trim('/').Split('/');
        bool IsSegment(int index, string expected) =>
            string.Equals(segments[index], expected, StringComparison.OrdinalIgnoreCase);

        return segments.Length == 3 && IsSegment(0, "api") && IsSegment(1, "admin") && IsSegment(2, "locations")
               || segments.Length == 6 && IsSegment(0, "api") && IsSegment(1, "admin") && IsSegment(2, "locations")
                   && IsSegment(4, "api-key") && IsSegment(5, "rotate");
    }
}
