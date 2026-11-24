namespace Features.Tests.Http;

/// <summary>
/// Every test class that starts a FastEndpoints host in memory, so they run one at a time. The rest of the suite still
/// runs in parallel.
/// FastEndpoints keeps its JSON options in one static (<c>Config.SerOpts.Options</c>). Each host's
/// <c>UseFastEndpoints</c> puts a new copy there and then changes it, and every host writes its responses with the
/// copy that is there at that moment. With two classes running at once, one host's response can lock the copy that
/// another host is still changing, and that host fails to start with "This JsonSerializerOptions instance is
/// read-only" (seen once in CI on 2026-11-24, on PR 68). A new class that starts a host belongs here too.
/// </summary>
[CollectionDefinition(Name)]
public sealed class FastEndpointsHostCollection
{
    public const string Name = "FastEndpoints hosts";
}
