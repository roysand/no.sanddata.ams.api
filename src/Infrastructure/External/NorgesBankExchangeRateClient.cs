using System.Globalization;
using System.Text.Json;
using Application.Common.Interfaces.External;

namespace Infrastructure.External;

/// <summary>
/// Norges Bank open data API client for the daily EUR/NOK spot exchange rate
/// (https://data.norges-bank.no/api/data/EXR/B.EUR.NOK.SP - see research.md §5). No
/// authentication required. Response shape verified against a live request during
/// implementation (unlike ENTSO-E, which needs a token that wasn't available yet).
/// </summary>
public class NorgesBankExchangeRateClient(HttpClient httpClient) : IExchangeRateClient
{
    public async Task<decimal?> GetRateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        string dateString = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string query = $"?format=sdmx-json&startPeriod={dateString}&endPeriod={dateString}&locale=en";

        using HttpResponseMessage response = await httpClient.GetAsync(query, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        JsonElement dataSets = document.RootElement.GetProperty("data").GetProperty("dataSets");
        if (dataSets.GetArrayLength() == 0)
        {
            return null;
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
                    return rate;
                }
            }
        }

        return null;
    }
}
