using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Application.Common.Interfaces.External;
using Domain.Common;
using Infrastructure.External.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.External;

public class EntsoeOptions
{
    public const string SectionName = "EntsoE";

    public required string SecurityToken { get; init; }
}

/// <summary>
/// ENTSO-E Transparency Platform client for day-ahead electricity spot prices
/// (https://web-api.tp.entsoe.eu/api, documentType=A44/processType=A01 - see research.md §4).
/// NOTE: parsing is namespace-agnostic (matches on element local names only) since the exact
/// published namespace URI couldn't be verified against a live response at implementation time
/// (ENTSO-E security tokens require a manual, multi-day approval process) - this is more
/// resilient to minor schema-version differences than hardcoding a namespace string unverified.
/// </summary>
public class EntsoeSpotPriceClient(
    HttpClient httpClient,
    IOptions<EntsoeOptions> options,
    ILogger<EntsoeSpotPriceClient> logger) : ISpotPriceClient
{
    private static readonly IReadOnlyDictionary<string, string> EicCodesByZone = new Dictionary<string, string>
    {
        ["NO1"] = "10YNO-1--------2",
        ["NO2"] = "10YNO-2--------T",
        ["NO3"] = "10YNO-3--------J",
        ["NO4"] = "10YNO-4--------9",
        ["NO5"] = "10Y1001A1001A48H"
    };

    private static readonly Error UnsupportedZone = Error.Validation(
        "SpotPrice.UnsupportedZone", "No ENTSO-E bidding zone is configured for the requested price region");
    private static readonly Error TokenMissing = Error.Problem(
        "SpotPrice.TokenMissing", "ENTSO-E security token is not configured");
    private static readonly Error Unavailable = Error.Problem(
        "SpotPrice.Unavailable", "ENTSO-E could not be reached");
    private static readonly Error InvalidResponse = Error.Problem(
        "SpotPrice.InvalidResponse", "ENTSO-E returned an unexpected response");
    private static readonly Error NoData = Error.NotFound(
        "SpotPrice.NoData", "ENTSO-E returned no usable prices for the requested period");

    public async Task<Result<IReadOnlyList<SpotPrice>>> GetPricesAsync(
        string priceRegion, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        if (!EicCodesByZone.TryGetValue(priceRegion, out string? eicCode))
        {
            return Result.Failure<IReadOnlyList<SpotPrice>>(UnsupportedZone);
        }

        // Without a token ENTSO-E rejects the call; skip it so the failure is logged once, clearly.
        if (string.IsNullOrWhiteSpace(options.Value.SecurityToken))
        {
            LogMessages.SpotPriceTokenMissing(logger, priceRegion);
            return Result.Failure<IReadOnlyList<SpotPrice>>(TokenMissing);
        }

        string periodStart = fromUtc.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        string periodEnd = toUtc.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);

        string query =
            $"?securityToken={options.Value.SecurityToken}" +
            "&documentType=A44" +
            "&processType=A01" +
            $"&in_Domain={eicCode}" +
            $"&out_Domain={eicCode}" +
            $"&periodStart={periodStart}" +
            $"&periodEnd={periodEnd}";

        try
        {
            // The request URL carries the security token, so only the status code is logged, never the URL.
            using HttpResponseMessage response = await httpClient.GetAsync(query, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogMessages.SpotPriceRequestFailed(logger, priceRegion, (int)response.StatusCode);
                return Result.Failure<IReadOnlyList<SpotPrice>>(Error.Problem(
                    "SpotPrice.HttpError", $"ENTSO-E responded with HTTP {(int)response.StatusCode}"));
            }

            string xml = await response.Content.ReadAsStringAsync(cancellationToken);
            IReadOnlyList<SpotPrice> prices = ParsePrices(xml);
            if (prices.Count == 0)
            {
                LogMessages.SpotPriceNoData(logger, priceRegion);
                return Result.Failure<IReadOnlyList<SpotPrice>>(NoData);
            }

            return Result.Success(prices);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   && !cancellationToken.IsCancellationRequested)
        {
            LogMessages.SpotPriceUnavailable(logger, priceRegion);
            return Result.Failure<IReadOnlyList<SpotPrice>>(Unavailable);
        }
        catch (Exception ex) when (ex is XmlException or FormatException)
        {
            LogMessages.SpotPriceInvalidResponse(logger, priceRegion);
            return Result.Failure<IReadOnlyList<SpotPrice>>(InvalidResponse);
        }
    }

    private static IReadOnlyList<SpotPrice> ParsePrices(string xml)
    {
        var points = new Dictionary<DateTime, decimal>();
        var document = XDocument.Parse(xml);

        foreach (XElement timeSeries in document.Descendants().Where(e => e.Name.LocalName == "TimeSeries"))
        {
            foreach (XElement period in timeSeries.Elements().Where(e => e.Name.LocalName == "Period"))
            {
                XElement? timeInterval = period.Elements().FirstOrDefault(e => e.Name.LocalName == "timeInterval");
                XElement? startElement = timeInterval?.Elements().FirstOrDefault(e => e.Name.LocalName == "start");
                XElement? resolutionElement = period.Elements().FirstOrDefault(e => e.Name.LocalName == "resolution");

                if (startElement is null || resolutionElement is null
                    || !DateTime.TryParse(startElement.Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime periodStart))
                {
                    continue;
                }

                var resolution = XmlConvert.ToTimeSpan(resolutionElement.Value);

                foreach (XElement point in period.Elements().Where(e => e.Name.LocalName == "Point"))
                {
                    XElement? positionElement = point.Elements().FirstOrDefault(e => e.Name.LocalName == "position");
                    XElement? priceElement = point.Elements().FirstOrDefault(e => e.Name.LocalName == "price.amount");

                    if (positionElement is null || priceElement is null
                        || !int.TryParse(positionElement.Value, CultureInfo.InvariantCulture, out int position)
                        || !decimal.TryParse(priceElement.Value, CultureInfo.InvariantCulture, out decimal price))
                    {
                        continue;
                    }

                    // Duplicate TimeSeries repeat the same points (verified live) - last one wins.
                    points[periodStart + resolution * (position - 1)] = price;
                }
            }
        }

        // Day-ahead prices are published per 15 minutes since the 15-min MTU go-live; our model is
        // hourly, so each hour's price is the average of its points (a single point for PT60M).
        return points
            .GroupBy(p => new DateTime(p.Key.Year, p.Key.Month, p.Key.Day, p.Key.Hour, 0, 0, DateTimeKind.Utc))
            .OrderBy(g => g.Key)
            .Select(g => new SpotPrice(g.Key, g.Average(p => p.Value)))
            .ToList();
    }
}
