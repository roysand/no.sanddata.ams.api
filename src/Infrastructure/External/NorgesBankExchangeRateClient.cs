using System.Globalization;
using System.Net;
using System.Text.Json;
using Application.Common.Interfaces.External;
using Domain.Common;
using Infrastructure.External.Logging;
using Microsoft.Extensions.Logging;

namespace Infrastructure.External;

/// <summary>
/// Norges Bank open data API client for the daily EUR/NOK spot exchange rate
/// (https://data.norges-bank.no/api/data/EXR/B.EUR.NOK.SP - see research.md §5). No
/// authentication required. Response shape verified against a live request during
/// implementation (unlike ENTSO-E, which needs a token that wasn't available yet).
/// </summary>
public class NorgesBankExchangeRateClient(
    HttpClient httpClient,
    ILogger<NorgesBankExchangeRateClient> logger) : IExchangeRateClient
{
    private static readonly Error NotPublished = Error.NotFound(
        "ExchangeRate.NotPublished", "No exchange rate has been published for the requested date");
    private static readonly Error Unavailable = Error.Problem(
        "ExchangeRate.Unavailable", "Norges Bank could not be reached");
    private static readonly Error InvalidResponse = Error.Problem(
        "ExchangeRate.InvalidResponse", "Norges Bank returned an unexpected response");

    public async Task<Result<decimal>> GetRateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        string dateString = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string query = $"?format=sdmx-json&startPeriod={dateString}&endPeriod={dateString}&locale=en";

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(query, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Verified live: for a date with no data (weekend/holiday, or today's rate not out yet)
                // Norges Bank answers 404 rather than an empty dataset.
                LogMessages.ExchangeRateNotPublished(logger, date);
                return Result.Failure<decimal>(NotPublished);
            }

            if (!response.IsSuccessStatusCode)
            {
                LogMessages.ExchangeRateRequestFailed(logger, (int)response.StatusCode);
                return Result.Failure<decimal>(Error.Problem(
                    "ExchangeRate.HttpError", $"Norges Bank responded with HTTP {(int)response.StatusCode}"));
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            JsonElement dataSets = document.RootElement.GetProperty("data").GetProperty("dataSets");
            if (dataSets.GetArrayLength() == 0)
            {
                // Norges Bank publishes business days only, and today's rate arrives in the afternoon.
                LogMessages.ExchangeRateNotPublished(logger, date);
                return Result.Failure<decimal>(NotPublished);
            }

            JsonElement series = dataSets[0].GetProperty("series");
            foreach (JsonProperty seriesEntry in series.EnumerateObject())
            {
                // The query is fully specified (one currency pair, one tenor, one date), so there's
                // at most one series and one observation - take the first one found.
                foreach (JsonProperty observation in seriesEntry.Value.GetProperty("observations").EnumerateObject())
                {
                    string? rateText = observation.Value.EnumerateArray().FirstOrDefault().GetString();
                    if (decimal.TryParse(rateText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rate))
                    {
                        return Result.Success(rate);
                    }
                }
            }

            LogMessages.ExchangeRateNotPublished(logger, date);
            return Result.Failure<decimal>(NotPublished);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   && !cancellationToken.IsCancellationRequested)
        {
            LogMessages.ExchangeRateUnavailable(logger);
            return Result.Failure<decimal>(Unavailable);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            LogMessages.ExchangeRateInvalidResponse(logger);
            return Result.Failure<decimal>(InvalidResponse);
        }
    }
}
