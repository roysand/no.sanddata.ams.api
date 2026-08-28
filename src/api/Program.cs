using Application;
using Domain.Common;
using FastEndpoints;
using Features;
using Infrastructure;
using Infrastructure.Logging;
using Infrastructure.Middleware;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add local.settings.json to configuration
builder.Configuration.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, context, cancellationToken) =>
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
    }));

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

// Add Authentication and Authorization middleware
app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
// Request/response logging middleware needs authentication to populate claims
app.UseMiddleware<RequestResponseLoggingMiddleware>();
app.UseAuthorization();

// Add exception handling middleware
app.UseExceptionHandling();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
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
app.Run();
