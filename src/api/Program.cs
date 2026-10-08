using Api.OpenApi;
using FastEndpoints;
using Features;
using Infrastructure;
using Infrastructure.Database;
using Infrastructure.Logging;
using Infrastructure.Middleware;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add local.settings.json to configuration
builder.Configuration.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);

// Configures console log timestamps (Logging:Console:FormatterOptions in appsettings.json).
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
builder.Services.Configure<Microsoft.Extensions.Logging.Console.SimpleConsoleFormatterOptions>(
    builder.Configuration.GetSection("Logging:Console:FormatterOptions"));

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        // Add security schemes to OpenAPI document
        document.Components ??= new Microsoft.OpenApi.OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>
        {
            ["Bearer"] = new Microsoft.OpenApi.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "JWT Authorization header using the Bearer scheme. Enter your token in the text input below."
            },
            ["ApiKey"] = new Microsoft.OpenApi.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.SecuritySchemeType.ApiKey,
                In = Microsoft.OpenApi.ParameterLocation.Header,
                Name = "X-API-Key",
                Description = "API Key authentication. Enter your API key in the text input below."
            }
        };
        return Task.CompletedTask;
    });
    options.AddOperationTransformer<FastEndpointsQueryParameterTransformer>();
});

builder.Services.AddFastEndpoints(options =>
    options.Assemblies = [typeof(Features.Users.Endpoints.CreateUserEndpoint).Assembly]);

// Add application services (CQRS handlers, etc.)
builder.Services.AddFeatures();

// Add Infrastructure services (DbContext, Repositories, Authentication, etc.)
builder.Services.AddInfrastructure(builder.Configuration);

// Bind request logging options from configuration
builder.Services.Configure<RequestLoggingOptions>(
    builder.Configuration.GetSection("RequestLogging"));

const string FrontendCorsPolicy = "Frontend";
builder.Services.AddCors(options =>
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .AllowAnyHeader()
            .AllowAnyMethod()));

WebApplication app = builder.Build();

// Caddy terminates TLS and reverse-proxies to this container over plain HTTP on the Docker network,
// so Kestrel never sees HTTPS directly - only Caddy's X-Forwarded-Proto header says so. Without this,
// Request.Scheme (and anything derived from it, e.g. the OpenAPI document's server URL) stays "http"
// even in production. KnownNetworks/KnownProxies are cleared because the proxy's container IP isn't
// static across deploys; that's safe here since the api container is never published on the host
// (compose.prod.yaml uses `expose`, not `ports`) - only containers on the same Docker network can
// reach it at all, so only Caddy can ever be the one setting these headers.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Add Authentication and Authorization middleware
app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
// Request/response logging middleware needs authentication to populate claims
app.UseMiddleware<RequestResponseLoggingMiddleware>();
app.UseAuthorization();

// Add exception handling middleware
app.UseExceptionHandling();

// Configure the HTTP request pipeline.
// Development always gets it; elsewhere it's an explicit opt-in (EnableScalarDocs) - e.g. production,
// gated by HTTP Basic Auth at the reverse proxy (see Caddyfile.example), since the app itself only has
// header-based JWT/API-key auth, which can't gate a plain browser page load.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("EnableScalarDocs"))
{
    app.MapScalarApiReference(options =>
    {
        options.Title = "AMS API Documentation";
        options.Theme = ScalarTheme.DeepSpace;
        options.DefaultOpenAllTags = true;
        options.Authentication = new ScalarAuthenticationOptions
        {
            PreferredSecuritySchemes = ["Bearer"],
        };
    });
    app.MapOpenApi();
}

if (app.Urls.Any(url => url.StartsWith("https", StringComparison.OrdinalIgnoreCase)))
{
    app.UseHttpsRedirection();
}

app.UseFastEndpoints();

// Before Run() so the schema exists before hosted services (e.g. AdminBootstrapService) start.
await app.ApplyMigrationsIfEnabledAsync();

app.Run();
