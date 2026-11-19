using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Application.Common.Interfaces.Security;
using Application.WaterHeaters;
using Domain.Common;
using Domain.Common.Entities;
using Features.Tests.NorgesPris;
using Features.WaterHeaters.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Features.Tests.WaterHeaters;

public class WaterHeaterSamplingServiceTests
{
    private readonly IWaterHeaterRepository<WaterHeater> _heaters = Substitute.For<IWaterHeaterRepository<WaterHeater>>();
    private readonly IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample> _samples =
        Substitute.For<IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>>();
    private readonly IWaterHeaterClient _client = Substitute.For<IWaterHeaterClient>();
    private readonly ISecretProtector _protector = Substitute.For<ISecretProtector>();
    private readonly List<WaterHeaterPowerSample> _inserted = [];

    private readonly WaterHeater _first = WaterHeaterTestData.Heater("aaaa", Guid.NewGuid());
    private readonly WaterHeater _second = WaterHeaterTestData.Heater("bbbb", Guid.NewGuid());

    public WaterHeaterSamplingServiceTests()
    {
        _samples.Insert(Arg.Do<WaterHeaterPowerSample>(_inserted.Add));
        _protector.Unprotect(Arg.Any<string>(), Arg.Any<Guid>()).Returns(Result.Success(OsoSamples.Key));
        _client.GetActualLoadAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(0.011774m));
        _heaters.GetForActiveLocationsAsync(Arg.Any<CancellationToken>()).Returns([_first, _second]);
    }

    private Task SampleOnce()
    {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(_heaters).AddSingleton(_samples).AddSingleton(_client).AddSingleton(_protector)
            .BuildServiceProvider();
        var service = new WaterHeaterSamplingService(
            Substitute.For<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            new FixedTimeProvider("2026-11-20T10:00:05.789Z"),
            NullLogger<WaterHeaterSamplingService>.Instance);
        return service.SampleOnceAsync(services, default);
    }

    private void Fail(WaterHeater heater, Error error) =>
        _client.GetActualLoadAsync(Arg.Is<WaterHeaterCredentials>(c => c.LocationId == heater.LocationId),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<decimal>(error));

    [Fact]
    public async Task EveryHeater_GetsOneSample_AtTheSecondItWasRead()
    {
        await SampleOnce();

        Assert.Equal(2, _inserted.Count);
        Assert.All(_inserted, s =>
        {
            Assert.Equal(new DateTime(2026, 11, 20, 10, 0, 5, DateTimeKind.Utc), s.SampledAtUtc);
            Assert.Equal(0.011774m, s.PowerKw);
        });
        Assert.Equal([_first.LocationId, _second.LocationId], _inserted.Select(s => s.LocationId));
        await _samples.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedHeater_LeavesAGap_AndTheOtherIsStillSampled()
    {
        Fail(_first, WaterHeaterErrors.Unavailable);

        await SampleOnce();

        WaterHeaterPowerSample only = Assert.Single(_inserted);
        Assert.Equal(_second.LocationId, only.LocationId);
        Assert.DoesNotContain(_inserted, s => s.PowerKw == 0);
    }

    [Fact]
    public async Task AnUnreadableKey_SkipsOsoForThatHeater()
    {
        _protector.Unprotect(_first.ProtectedSubscriptionKey, _first.LocationId)
            .Returns(Result.Failure<string>(WaterHeaterErrors.KeyUnreadable));

        await SampleOnce();

        await _client.DidNotReceive().GetActualLoadAsync(
            Arg.Is<WaterHeaterCredentials>(c => c.LocationId == _first.LocationId), Arg.Any<CancellationToken>());
        Assert.Single(_inserted);
    }

    [Fact]
    public async Task AnExceptionForOneHeater_DoesNotStopTheOthers()
    {
        _client.GetActualLoadAsync(Arg.Is<WaterHeaterCredentials>(c => c.LocationId == _first.LocationId),
                Arg.Any<CancellationToken>())
            .Returns<Task<Result<decimal>>>(_ => throw new InvalidOperationException("boom"));

        await SampleOnce();

        Assert.Equal(_second.LocationId, Assert.Single(_inserted).LocationId);
    }

    [Fact]
    public async Task AllFailed_SavesNothing()
    {
        Fail(_first, WaterHeaterErrors.Rejected);
        Fail(_second, WaterHeaterErrors.Unavailable);

        await SampleOnce();

        Assert.Empty(_inserted);
        await _samples.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoHeaters_SavesNothing()
    {
        _heaters.GetForActiveLocationsAsync(Arg.Any<CancellationToken>()).Returns([]);

        await SampleOnce();

        await _samples.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
