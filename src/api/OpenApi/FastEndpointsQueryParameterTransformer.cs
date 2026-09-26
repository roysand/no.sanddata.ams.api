using System.Text.RegularExpressions;
using FastEndpoints;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Api.OpenApi;

/// <summary>
/// FastEndpoints binds a GET request's route and query parameters itself, invisibly to ASP.NET
/// Core's ApiExplorer/native OpenAPI reflection - so without this, every FastEndpoints GET
/// endpoint with a request DTO shows up with no parameters at all in the generated document (and
/// therefore none in Scalar's UI either), plus a spurious request body (native reflection treats
/// the DTO as an accepted JSON body regardless of verb, but these GET endpoints never read one).
/// This reflects over the endpoint's request DTO (available via FastEndpoints' own
/// <see cref="EndpointDefinition"/> metadata), adds one parameter per public property - as a
/// path parameter if its name appears in the route template (e.g. "id" in "/api/users/{id}"),
/// otherwise as a query parameter - and removes the incorrect request body.
/// </summary>
public partial class FastEndpointsQueryParameterTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        EndpointDefinition? endpointDefinition = context.Description.ActionDescriptor.EndpointMetadata?
            .OfType<EndpointDefinition>()
            .FirstOrDefault();

        if (endpointDefinition?.ReqDtoType is null || !endpointDefinition.Verbs.Contains("GET"))
        {
            return Task.CompletedTask;
        }

        operation.RequestBody = null;

        var existingNames = (operation.Parameters ?? [])
            .Select(p => p.Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var routeParamNames = RouteParamPattern()
            .Matches(context.Description.RelativePath ?? string.Empty)
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        operation.Parameters ??= [];

        foreach (System.Reflection.PropertyInfo property in endpointDefinition.ReqDtoType.GetProperties())
        {
            if (existingNames.Contains(property.Name))
            {
                continue;
            }

            bool isRouteParam = routeParamNames.Contains(property.Name);
            (JsonSchemaType schemaType, string? format) = MapType(property.PropertyType);
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = ToCamelCase(property.Name),
                In = isRouteParam ? ParameterLocation.Path : ParameterLocation.Query,
                Required = isRouteParam || property.PropertyType == typeof(Guid),
                Schema = new OpenApiSchema { Type = schemaType, Format = format }
            });
        }

        return Task.CompletedTask;
    }

    [GeneratedRegex(@"\{(?<name>\w+)(:[^}]+)?\}")]
    private static partial Regex RouteParamPattern();

    private static (JsonSchemaType Type, string? Format) MapType(Type propertyType)
    {
        Type type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        bool nullable = type != propertyType;

        (JsonSchemaType baseType, string? format) = type switch
        {
            _ when type == typeof(Guid) => (JsonSchemaType.String, "uuid"),
            _ when type == typeof(DateTime) => (JsonSchemaType.String, "date-time"),
            _ when type == typeof(int) => (JsonSchemaType.Integer, "int32"),
            _ when type == typeof(long) => (JsonSchemaType.Integer, "int64"),
            _ when type == typeof(bool) => (JsonSchemaType.Boolean, null),
            _ => (JsonSchemaType.String, null)
        };

        return (nullable ? baseType | JsonSchemaType.Null : baseType, format);
    }

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
