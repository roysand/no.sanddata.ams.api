using System.Net;
using System.Text;

namespace Features.Tests.WaterHeaters;

/// <summary>Answers OSO calls by the last path segment, and records what was asked.</summary>
internal sealed class OsoStubHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _answers = new(StringComparer.Ordinal)
    {
        ["Status"] = (HttpStatusCode.OK, OsoSamples.Status),
        ["CurrentTemperature"] = (HttpStatusCode.OK, OsoSamples.CurrentTemperature),
        ["ActualLoad"] = (HttpStatusCode.OK, OsoSamples.ActualLoad),
        ["TappingCapacityKwh"] = (HttpStatusCode.OK, OsoSamples.TappingCapacityKwh),
    };

    public List<(string Path, string? Key)> Requests { get; } = [];
    public Exception? Throw { get; set; }
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public OsoStubHandler Answer(string call, HttpStatusCode status, string body = "{}")
    {
        _answers[call] = (status, body);
        return this;
    }

    public HttpClient CreateClient(TimeSpan? timeout = null) => new(this)
    {
        BaseAddress = new Uri("https://api.osoenergy.no/water-heater-api/"),
        Timeout = timeout ?? TimeSpan.FromSeconds(10)
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string? key = request.Headers.TryGetValues("Ocp-Apim-Subscription-Key", out IEnumerable<string>? values)
            ? values.Single()
            : null;
        lock (Requests)
        {
            Requests.Add((request.RequestUri!.AbsolutePath, key));
        }

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, ct);
        }

        if (Throw is not null)
        {
            throw Throw;
        }

        string call = request.RequestUri!.Segments[^1];
        (HttpStatusCode status, string body) = _answers[call];
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
