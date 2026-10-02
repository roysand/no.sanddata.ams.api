using System.Net;
using Application.Common.Interfaces.External;
using Infrastructure.External;
using Microsoft.Extensions.Options;

namespace Features.Tests.ElectricityCost;

public class EntsoeSpotPriceClientTests
{
    private static readonly DateTime From = new(2026, 10, 1, 22, 0, 0, DateTimeKind.Utc);

    private static string Series(string start, string resolution, params decimal[] prices) =>
        "<TimeSeries><Period>" +
        $"<timeInterval><start>{start}</start><end>x</end></timeInterval><resolution>{resolution}</resolution>" +
        string.Concat(prices.Select((p, i) =>
            $"<Point><position>{i + 1}</position><price.amount>{p.ToString(System.Globalization.CultureInfo.InvariantCulture)}</price.amount></Point>")) +
        "</Period></TimeSeries>";

    private static string Document(params string[] series) =>
        "<Publication_MarketDocument xmlns=\"urn:iec62325.351:tc57wg16:451-3:publicationdocument:7:3\">" +
        string.Concat(series) + "</Publication_MarketDocument>";

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static (EntsoeSpotPriceClient Client, StubHandler Handler) CreateClient(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api") };
        IOptions<EntsoeOptions> options = Options.Create(new EntsoeOptions { SecurityToken = "token" });
        return (new EntsoeSpotPriceClient(http, options), handler);
    }

    [Fact]
    public async Task GetPrices_QuarterHourPoints_AveragedToHourlyPrice()
    {
        string xml = Document(Series("2026-10-01T22:00Z", "PT15M", 100m, 110m, 120m, 130m, 200m, 200m, 200m, 200m));
        (EntsoeSpotPriceClient client, _) = CreateClient(HttpStatusCode.OK, xml);

        IReadOnlyList<SpotPrice> prices = await client.GetPricesAsync("NO1", From, From.AddHours(2), CancellationToken.None);

        Assert.Equal(2, prices.Count);
        Assert.Equal(From, prices[0].HourStartUtc);
        Assert.Equal(115m, prices[0].PriceEurPerMwh);
        Assert.Equal(From.AddHours(1), prices[1].HourStartUtc);
        Assert.Equal(200m, prices[1].PriceEurPerMwh);
    }

    [Fact]
    public async Task GetPrices_DuplicateTimeSeries_AreDeduplicated()
    {
        string one = Series("2026-10-01T22:00Z", "PT15M", 100m, 100m, 100m, 100m);
        (EntsoeSpotPriceClient client, _) = CreateClient(HttpStatusCode.OK, Document(one, one));

        IReadOnlyList<SpotPrice> prices = await client.GetPricesAsync("NO1", From, From.AddHours(1), CancellationToken.None);

        Assert.Single(prices);
        Assert.Equal(100m, prices[0].PriceEurPerMwh);
    }

    [Fact]
    public async Task GetPrices_HourlyResolution_ReturnsOnePricePerPoint()
    {
        string xml = Document(Series("2026-10-01T22:00Z", "PT60M", 90m, 95m));
        (EntsoeSpotPriceClient client, _) = CreateClient(HttpStatusCode.OK, xml);

        IReadOnlyList<SpotPrice> prices = await client.GetPricesAsync("NO1", From, From.AddHours(2), CancellationToken.None);

        Assert.Equal([90m, 95m], prices.Select(p => p.PriceEurPerMwh));
    }

    [Fact]
    public async Task GetPrices_UnknownZone_ReturnsEmptyWithoutCallingApi()
    {
        (EntsoeSpotPriceClient client, StubHandler handler) = CreateClient(HttpStatusCode.OK, Document());

        var prices = await client.GetPricesAsync("SE3", From, From.AddHours(1), CancellationToken.None);

        Assert.Empty(prices);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task GetPrices_ErrorResponse_ReturnsEmpty()
    {
        (EntsoeSpotPriceClient client, _) = CreateClient(HttpStatusCode.Unauthorized, "denied");

        IReadOnlyList<SpotPrice> prices = await client.GetPricesAsync("NO1", From, From.AddHours(1), CancellationToken.None);

        Assert.Empty(prices);
    }

    [Fact]
    public async Task GetPrices_BuildsQueryWithEicCodeAndUtcPeriod()
    {
        (EntsoeSpotPriceClient client, StubHandler handler) = CreateClient(HttpStatusCode.OK, Document());

        await client.GetPricesAsync("NO1", From, From.AddDays(1), CancellationToken.None);

        string query = handler.LastRequest!.Query;
        Assert.Contains("in_Domain=10YNO-1--------2", query);
        Assert.Contains("periodStart=202610012200", query);
        Assert.Contains("periodEnd=202610022200", query);
        Assert.Contains("documentType=A44", query);
    }
}
