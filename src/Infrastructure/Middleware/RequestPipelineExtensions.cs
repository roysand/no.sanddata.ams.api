using Microsoft.AspNetCore.Builder;

namespace Infrastructure.Middleware;

/// <summary>
/// The request steps between forwarded headers and the endpoints, in the order the API depends on. Shared by
/// Program.cs and the pipeline tests so they run the same order. The exception handler covers everything from
/// authorization inward, including the API-key database lookup; CORS, sign-in and the request log run before it.
/// </summary>
public static class RequestPipelineExtensions
{
    public static IApplicationBuilder UseRequestPipeline(this IApplicationBuilder app, string corsPolicyName)
    {
        app.UseCors(corsPolicyName);
        app.UseAuthentication();

        // The request log needs authentication to have run, so it can record the caller's claims. It stays
        // outside the exception handler, so it records the fixed 500 the handler writes.
        app.UseMiddleware<RequestResponseLoggingMiddleware>();

        // Before authorization: authorization runs the API-key scheme, which reads the database (spec 014).
        app.UseExceptionHandling();
        app.UseAuthorization();

        return app;
    }
}
