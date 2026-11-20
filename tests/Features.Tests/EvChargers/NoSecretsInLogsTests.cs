using System.Net;
using System.Text.Json;
using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.EvChargers.Handlers;
using Features.EvChargers.Queries;
using Features.EvChargers.Services;
using Infrastructure;
using Infrastructure.External;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.EvChargers;

/// <summary>
/// SC-006 / FR-018: the Zaptec user name, password and access token never reach a log entry or a response. Runs the
/// real Zaptec client (registered exactly as AddInfrastructure does, so RemoveAllLoggers is covered), the admin list
/// handler and the read job, through success and every kind of failure, with every log entry captured.
/// </summary>
public class NoSecretsInLogsTests
{
    private readonly CapturingLoggerProvider _logs = new();
    private readonly ZaptecStubHandler _zaptec = new();

    private ServiceProvider Build()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApplicationSettings:DbConnectionString"] = "Host=localhost;Database=unused",
            ["Zaptec:Username"] = ZaptecSamples.Username,
            ["Zaptec:Password"] = ZaptecSamples.Password,
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging(b => b.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddProvider(_logs));
        services.AddInfrastructure(configuration);
        services.AddHttpClient<IEvChargerClient, ZaptecEvChargerClient>().ConfigurePrimaryHttpMessageHandler(() => _zaptec);
        return services.BuildServiceProvider();
    }

    private void AssertNoSecrets(params object[] responses)
    {
        string[] secrets = [ZaptecSamples.Username, ZaptecSamples.Password, ZaptecSamples.Token,
            Uri.EscapeDataString(ZaptecSamples.Username), Uri.EscapeDataString(ZaptecSamples.Password)];
        foreach (string secret in secrets)
        {
            Assert.DoesNotContain(_logs.Lines, l => l.Contains(secret, StringComparison.OrdinalIgnoreCase));
            foreach (object response in responses)
            {
                Assert.DoesNotContain(secret, JsonSerializer.Serialize(response));
            }
        }
    }

    public static TheoryData<string> Cases => ["ok", "token-401", "data-401", "data-503", "bad-json", "network"];

    private void Arrange(string scenario)
    {
        switch (scenario)
        {
            case "token-401":
                _zaptec.Answer(ZaptecStubHandler.TokenPath, HttpStatusCode.Unauthorized,
                    $"{{\"error\":\"invalid_grant for {ZaptecSamples.Username}\"}}");
                break;
            case "data-401":
                _zaptec.Answer(ZaptecStubHandler.ChargersPath, HttpStatusCode.Unauthorized);
                _zaptec.Answer(ZaptecStubHandler.SessionsPath, HttpStatusCode.Unauthorized);
                break;
            case "data-503":
                _zaptec.Answer(ZaptecStubHandler.ChargersPath, HttpStatusCode.ServiceUnavailable);
                _zaptec.Answer(ZaptecStubHandler.SessionsPath, HttpStatusCode.ServiceUnavailable);
                break;
            case "bad-json":
                _zaptec.Answer(ZaptecStubHandler.ChargersPath, HttpStatusCode.OK, $"{{\"Data\": [{ZaptecSamples.Token}");
                _zaptec.Answer(ZaptecStubHandler.SessionsPath, HttpStatusCode.OK, $"{{\"sessions\": [{ZaptecSamples.Token}");
                break;
            case "network":
                _zaptec.Throw = new HttpRequestException(
                    $"connection failed for {ZaptecSamples.Username}:{ZaptecSamples.Password} {ZaptecSamples.Token}");
                break;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ListingChargers_LogsNoSecrets(string scenario)
    {
        Arrange(scenario);
        await using ServiceProvider sp = Build();

        Result<IReadOnlyList<ZaptecChargerResponse>> result =
            await new ListZaptecChargersQueryHandler(sp.GetRequiredService<IEvChargerClient>())
                .Handle(new ListZaptecChargersQuery(), default);

        Assert.Equal(scenario == "ok", result.IsSuccess);
        AssertNoSecrets(result.IsSuccess ? result.Value : result.Error);
        Assert.Contains(_zaptec.Requests, r => r.Path == ZaptecStubHandler.TokenPath);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TheReadJob_LogsNoSecrets(string scenario)
    {
        Arrange(scenario);
        await using ServiceProvider sp = Build();

        IEvChargerRepository<EvCharger> chargers = Substitute.For<IEvChargerRepository<EvCharger>>();
        chargers.GetForActiveLocationsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new EvCharger(Guid.NewGuid(), Guid.NewGuid(), ZaptecSamples.ChargerId, "Test charger",
                ZaptecSamples.InstallationId, "Test installation")
        ]);
        ServiceProvider scope = new ServiceCollection()
            .AddSingleton(chargers)
            .AddSingleton(Substitute.For<IChargingSessionRepository>())
            .AddSingleton(sp.GetRequiredService<IEvChargerClient>())
            .BuildServiceProvider();

        var job = new EvChargerReadService(
            Substitute.For<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), TimeProvider.System,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<EvChargerReadService>());
        await job.ReadOnceAsync(scope, default);

        Assert.NotEmpty(_logs.Lines);
        AssertNoSecrets();
    }

    [Fact]
    public void TheOptions_NeverPrintTheLogin()
    {
        var options = new ZaptecOptions { Username = ZaptecSamples.Username, Password = ZaptecSamples.Password };

        Assert.DoesNotContain(ZaptecSamples.Username, options.ToString());
        Assert.DoesNotContain(ZaptecSamples.Password, options.ToString());
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                string values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join(";", pairs.Select(p => $"{p.Key}={p.Value}"))
                    : string.Empty;
                lock (provider.Lines)
                {
                    provider.Lines.Add($"{category}|{formatter(state, exception)}|{values}|{exception}");
                }
            }
        }
    }
}
