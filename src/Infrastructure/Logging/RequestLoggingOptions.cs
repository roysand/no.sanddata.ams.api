namespace Infrastructure.Logging;

public sealed class RequestLoggingOptions
{
    /// <summary>
    /// Value substituted for sensitive header values (e.g. Authorization, X-API-Key) so secrets never reach the logs.
    /// </summary>
    public string MaskValue { get; set; } = "***";

    /// <summary>
    /// If true, request bodies will be read and included (use with caution).
    /// </summary>
    public bool LogRequestBody { get; set; } = false;

    /// <summary>
    /// If true, response bodies will be read and included (use with caution).
    /// </summary>
    public bool LogResponseBody { get; set; } = false;
}

