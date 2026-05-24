using System;

namespace Infrastructure.Logging;

public sealed class RequestLoggingOptions
{
    /// <summary>
    /// Attribute names to include with full values in logs. Known names: Path, Method, QueryString, UserAgent, RemoteIp, UserId, Email, Headers
    /// Any known attribute not present in this list will be logged with the MaskValue.
    /// </summary>
    public string[] AttributesToLog { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Value to use when an attribute is present but not allowed to be logged in full.
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

