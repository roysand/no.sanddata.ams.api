using System.Net;
using System.Text;

namespace Features.Tests.EvChargers;

/// <summary>
/// Answers Zaptec calls by path (token, chargers, sessions; the sessions call by its cursor), and records what was
/// asked. Each answer is a queue: the last one repeats once the others are used.
/// </summary>
internal sealed class ZaptecStubHandler : HttpMessageHandler
{
    public const string TokenPath = "/oauth/token";
    public const string ChargersPath = "/api/chargers";
    public const string SessionsPath = "/api/sessions/archived";
    public const string SessionsPage2Key = SessionsPath + "?cursor";
    public static readonly string StatePath = $"/api/chargers/{ZaptecSamples.ChargerId}/state";

    private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Body)>> _answers = new()
    {
        [TokenPath] = new([(HttpStatusCode.OK, ZaptecSamples.TokenAnswer)]),
        [ChargersPath] = new([(HttpStatusCode.OK, ZaptecSamples.Chargers)]),
        [SessionsPath] = new([(HttpStatusCode.OK, ZaptecSamples.SessionsPage1)]),
        [SessionsPage2Key] = new([(HttpStatusCode.OK, ZaptecSamples.SessionsPage2)]),
        [StatePath] = new([(HttpStatusCode.OK, ZaptecSamples.StateCharging)]),
    };

    public List<Recorded> Requests { get; } = [];
    public Exception? Throw { get; set; }
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public sealed record Recorded(string Path, string Query, string? Authorization, string? UserAgent, string? Body);

    /// <summary>Replaces the answers for a path; they are given in order, and the last one repeats.</summary>
    public ZaptecStubHandler Answer(string key, params (HttpStatusCode Status, string Body)[] answers)
    {
        _answers[key] = new Queue<(HttpStatusCode, string)>(answers);
        return this;
    }

    public ZaptecStubHandler Answer(string key, HttpStatusCode status, string body = "{}") => Answer(key, (status, body));

    public HttpClient CreateClient(TimeSpan? timeout = null) => new(this)
    {
        BaseAddress = new Uri("https://api.zaptec.com/"),
        Timeout = timeout ?? TimeSpan.FromSeconds(15)
    };

    public int Count(string path) => Requests.Count(r => r.Path == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        string path = request.RequestUri!.AbsolutePath;
        string query = request.RequestUri.Query;
        lock (Requests)
        {
            Requests.Add(new Recorded(
                path,
                query,
                request.Headers.Authorization?.ToString(),
                request.Headers.UserAgent.ToString(),
                body));
        }

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, ct);
        }

        if (Throw is not null)
        {
            throw Throw;
        }

        string key = path == SessionsPath && query.Contains("cursor=", StringComparison.Ordinal) ? SessionsPage2Key : path;
        Queue<(HttpStatusCode Status, string Body)> queue = _answers[key];
        (HttpStatusCode status, string answer) = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(answer, Encoding.UTF8, "application/json")
        };
        if (status == HttpStatusCode.TooManyRequests)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
        }

        return response;
    }
}
