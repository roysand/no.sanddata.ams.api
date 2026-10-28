using System.Net;
using System.Text;
using Application.CQRS;
using Domain.Common;
using Features.Users.Commands;
using NSubstitute;

namespace Features.Tests.Http;

/// <summary>
/// PUT /api/users/{id}/locations/{locationId} has an OPTIONAL body. The first version of the role feature
/// answered 415 (and then 400) to a PUT without a body, which no handler test could see.
/// </summary>
public class LinkUserLocationHttpTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid LocationId = Guid.NewGuid();
    private static readonly string Url = $"/api/users/{UserId}/locations/{LocationId}";

    private static Task<EndpointHost> StartHost() =>
        EndpointHost.StartAsync(dispatcher =>
            dispatcher
                .Send(Arg.Any<ICommand<Result<UserLocationChangeResponse>>>(), Arg.Any<CancellationToken>())
                .Returns(Result.Success(new UserLocationChangeResponse(true))));

    private static async Task<(HttpStatusCode Status, LinkUserLocationCommand? Command)> PutAsync(
        HttpContent? content)
    {
        await using EndpointHost host = await StartHost();
        HttpResponseMessage response = await host.Client.PutAsync(Url, content);
        LinkUserLocationCommand? command = host.Dispatcher.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<LinkUserLocationCommand>()
            .SingleOrDefault();
        return (response.StatusCode, command);
    }

    [Fact]
    public async Task Put_WithNoBodyAndNoContentType_LinksTheUserAsOwner()
    {
        var (status, command) = await PutAsync(null);

        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.Equal(LocationRole.Owner, command!.Role);
        Assert.Equal(UserId, command.UserId);
        Assert.Equal(LocationId, command.LocationId);
    }

    [Fact]
    public async Task Put_WithAJsonContentTypeButAnEmptyBody_LinksTheUserAsOwner()
    {
        var (status, command) = await PutAsync(new StringContent("", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.Equal(LocationRole.Owner, command!.Role);
    }

    [Fact]
    public async Task Put_WithARole_UsesIt()
    {
        var (status, command) = await PutAsync(
            new StringContent("""{ "role": "Viewer" }""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.Equal(LocationRole.Viewer, command!.Role);
    }

    [Theory]
    [InlineData("""{ "role": "Admin" }""")]
    [InlineData("""{ "role": "1" }""")]
    [InlineData("""{ "role": """)]
    public async Task Put_WithAnInvalidRoleOrBrokenJson_IsRefusedBeforeTheHandler(string body)
    {
        var (status, command) = await PutAsync(new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Null(command);
    }
}
