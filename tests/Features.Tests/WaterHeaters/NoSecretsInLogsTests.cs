using System.Net;
using System.Text.Json;
using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Application.Common.Interfaces.Security;
using Domain.Common;
using Domain.Common.Entities;
using Features.Tests.NorgesPris;
using Features.WaterHeaters.Commands;
using Features.WaterHeaters.Handlers;
using Features.WaterHeaters.Queries;
using Features.WaterHeaters.Services;
using Infrastructure;
using Infrastructure.External;
using Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using static Features.Tests.WaterHeaters.WaterHeaterTestData;

namespace Features.Tests.WaterHeaters;

/// <summary>
/// SC-006 / FR-016: the OSO key and the device id never reach a log entry or a response. Runs the connect, read and
/// sample paths through the real OSO client (registered exactly as AddInfrastructure does, so RemoveAllLoggers is
/// covered), the real protector and the real handlers, with every log entry captured.
/// </summary>
public class NoSecretsInLogsTests
{
    private readonly CapturingLoggerProvider _logs = new();
    private readonly OsoStubHandler _oso = new();

    private ServiceProvider Build(bool removeFactoryLoggers = true)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApplicationSettings:DbConnectionString"] = "Host=localhost;Database=unused",
            [AesGcmSecretProtector.ConfigKey] = AesGcmSecretProtectorTests.NewKey(),
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging(b => b.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddProvider(_logs));
        if (removeFactoryLoggers)
        {
            services.AddInfrastructure(configuration);
            services.AddHttpClient<IWaterHeaterClient, OsoWaterHeaterClient>().ConfigurePrimaryHttpMessageHandler(() => _oso);
        }
        else
        {
            // The control case: the same client without RemoveAllLoggers.
            services.AddHttpClient<IWaterHeaterClient, OsoWaterHeaterClient>(c =>
                    c.BaseAddress = new Uri("https://api.osoenergy.no/water-heater-api/"))
                .ConfigurePrimaryHttpMessageHandler(() => _oso);
        }

        return services.BuildServiceProvider();
    }

    private void AssertNoSecrets(params object[] responses)
    {
        Assert.NotEmpty(_logs.Lines);
        Assert.DoesNotContain(_logs.Lines, l => l.Contains(OsoSamples.Key, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_logs.Lines, l => l.Contains(OsoSamples.DeviceId, StringComparison.OrdinalIgnoreCase));
        foreach (object response in responses)
        {
            string json = JsonSerializer.Serialize(response);
            Assert.DoesNotContain(OsoSamples.Key, json);
            Assert.DoesNotContain(OsoSamples.DeviceId, json);
        }
    }

    [Fact]
    public async Task Control_WithoutRemoveAllLoggers_TheFactoryLogsTheDeviceId()
    {
        await using ServiceProvider sp = Build(removeFactoryLoggers: false);

        await sp.GetRequiredService<IWaterHeaterClient>().GetActualLoadAsync(
            new(LocationId, OsoSamples.DeviceId, OsoSamples.Key), default);

        Assert.Contains(_logs.Lines, l => l.Contains(OsoSamples.DeviceId, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task ConnectAndReadState_LogNoSecrets(HttpStatusCode osoAnswer)
    {
        if (osoAnswer != HttpStatusCode.OK)
        {
            _oso.Answer("Status", osoAnswer);
        }

        await using ServiceProvider sp = Build();
        ISecretProtector protector = sp.GetRequiredService<ISecretProtector>();
        IWaterHeaterClient client = sp.GetRequiredService<IWaterHeaterClient>();
        ILoggerFactory loggers = sp.GetRequiredService<ILoggerFactory>();

        var locations = Substitute.For<ILocationRepository<Location>>();
        locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns(Location());
        locations.IsUserAssociatedAsync(UserId, LocationId, Arg.Any<CancellationToken>()).Returns(true);
        var heaters = Substitute.For<IWaterHeaterRepository<WaterHeater>>();
        WaterHeater? saved = null;
        heaters.Insert(Arg.Do<WaterHeater>(h => saved = h));

        Result<WaterHeaterSettingsResponse> connected = await new ConnectWaterHeaterCommandHandler(
                locations, heaters, client, protector, loggers.CreateLogger<ConnectWaterHeaterCommandHandler>())
            .Handle(new ConnectWaterHeaterCommand(UserId, LocationId, OsoSamples.Key, OsoSamples.DeviceId), default);

        // Read the state with a stored heater, whatever connect did.
        heaters.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(
            saved ?? new WaterHeater(Guid.NewGuid(), LocationId, OsoSamples.DeviceId,
                protector.Protect(OsoSamples.Key, LocationId)));
        Result<WaterHeaterStateResponse> state = await new GetWaterHeaterStateQueryHandler(
                locations, heaters, client, protector, new FixedTimeProvider("2026-11-19T18:08:31Z"),
                loggers.CreateLogger<GetWaterHeaterStateQueryHandler>())
            .Handle(new GetWaterHeaterStateQuery(UserId, false, LocationId), default);

        Assert.Equal(osoAnswer == HttpStatusCode.OK, connected.IsSuccess);
        Assert.Equal(osoAnswer == HttpStatusCode.OK, state.IsSuccess);
        AssertNoSecrets(
            connected.IsSuccess ? connected.Value : connected.Error,
            state.IsSuccess ? state.Value : state.Error);
        Assert.DoesNotContain(OsoSamples.Key, saved?.ProtectedSubscriptionKey ?? string.Empty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sampling_LogsNoSecrets(bool osoDown)
    {
        if (osoDown)
        {
            _oso.Throw = new HttpRequestException($"connection failed for {OsoSamples.DeviceId}");
        }

        await using ServiceProvider sp = Build();
        ISecretProtector protector = sp.GetRequiredService<ISecretProtector>();
        var heaters = Substitute.For<IWaterHeaterRepository<WaterHeater>>();
        heaters.GetForActiveLocationsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new WaterHeater(Guid.NewGuid(), LocationId, OsoSamples.DeviceId, protector.Protect(OsoSamples.Key, LocationId))
        ]);
        ServiceProvider scope = new ServiceCollection()
            .AddSingleton(heaters)
            .AddSingleton(Substitute.For<IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>>())
            .AddSingleton(sp.GetRequiredService<IWaterHeaterClient>())
            .AddSingleton(protector)
            .BuildServiceProvider();

        var sampler = new WaterHeaterSamplingService(
            Substitute.For<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), TimeProvider.System,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<WaterHeaterSamplingService>());
        await sampler.SampleOnceAsync(scope, default);

        if (osoDown)
        {
            AssertNoSecrets();
        }
        else
        {
            // A success logs nothing in the sampler; the check is that nothing logged anywhere holds a secret.
            Assert.DoesNotContain(_logs.Lines, l => l.Contains(OsoSamples.Key) || l.Contains(OsoSamples.DeviceId));
        }
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
