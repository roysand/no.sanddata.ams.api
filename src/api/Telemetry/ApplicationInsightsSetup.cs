using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Logs;

namespace Api.Telemetry;

public static class ApplicationInsightsSetup
{
    // Database logs stay in the console (docker logs on the server) and are never sent to Application Insights.
    private static readonly string[] DatabaseLogCategories = ["Microsoft.EntityFrameworkCore", "Npgsql"];

    /// <summary>
    /// Sends logs, requests and outgoing HTTP calls to Application Insights when a connection string is set
    /// (APPLICATIONINSIGHTS_CONNECTION_STRING). Without one the API runs with console logging only.
    /// PostgreSQL calls are not recorded: the Azure Monitor distro only instruments SqlClient, not Npgsql.
    /// </summary>
    public static WebApplicationBuilder AddApplicationInsights(this WebApplicationBuilder builder)
    {
        string? connectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
            ?? builder.Configuration["AzureMonitor:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return builder;
        }

        builder.Services.AddOpenTelemetry().UseAzureMonitor(options => options.ConnectionString = connectionString);

        foreach (string category in DatabaseLogCategories)
        {
            builder.Logging.AddFilter<OpenTelemetryLoggerProvider>(category, LogLevel.None);
        }

        return builder;
    }
}
