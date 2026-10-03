using System.Net;
using Domain.Common;
using Infrastructure.External;
using Microsoft.Extensions.Logging.Abstractions;

namespace Features.Tests.ElectricityCost;

public class NorgesBankExchangeRateClientTests
{
    private static readonly DateOnly Date = new(2026, 10, 2);

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private static NorgesBankExchangeRateClient CreateClient(HttpStatusCode status, string body) =>
        new(new HttpClient(new StubHandler(status, body)) { BaseAddress = new Uri("https://example.test/api") },
            NullLogger<NorgesBankExchangeRateClient>.Instance);

    [Fact]
    public async Task GetRate_PublishedDate_ReturnsRate()
    {
        const string json = """{"data":{"dataSets":[{"series":{"0:0:0:0":{"observations":{"0":["10.8315"]}}}}]}}""";

        Result<decimal> result = await CreateClient(HttpStatusCode.OK, json).GetRateAsync(Date, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(10.8315m, result.Value);
    }

    [Fact]
    public async Task GetRate_NotFoundResponse_IsNotPublishedNotAnError()
    {
        // Norges Bank answers 404 for weekends/holidays and for today's rate before it is published.
        Result<decimal> result = await CreateClient(HttpStatusCode.NotFound, "{}").GetRateAsync(Date, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("ExchangeRate.NotPublished", result.Error.Code);
    }

    [Fact]
    public async Task GetRate_EmptyDataSets_IsNotPublished()
    {
        Result<decimal> result = await CreateClient(HttpStatusCode.OK, """{"data":{"dataSets":[]}}""")
            .GetRateAsync(Date, CancellationToken.None);

        Assert.Equal("ExchangeRate.NotPublished", result.Error.Code);
    }

    [Fact]
    public async Task GetRate_ServerError_FailsWithHttpError()
    {
        Result<decimal> result = await CreateClient(HttpStatusCode.InternalServerError, "oops")
            .GetRateAsync(Date, CancellationToken.None);

        Assert.Equal("ExchangeRate.HttpError", result.Error.Code);
    }
}
