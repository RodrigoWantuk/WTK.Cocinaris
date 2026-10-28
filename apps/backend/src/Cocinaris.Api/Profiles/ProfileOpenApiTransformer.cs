using System.Text.Json.Nodes;
using Cocinaris.Modules.Profiles.Application;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Cocinaris.Api.Profiles;

/// <summary>Adds profile operations and schemas to the Cocinaris OpenAPI document.</summary>
internal static class ProfileOpenApiTransformer
{
    private static readonly string[] FieldActions = ProfileFieldMutationRules.ValidActions.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    private static readonly string[] FieldDurabilities = ProfileFieldMutationRules.ValidDurabilities.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    private static readonly string[] PreferenceActions = ["add", "remove", "update"];

    /// <summary>Applies profile-specific OpenAPI metadata.</summary>
    internal static Task ApplyAsync(OpenApiDocument document, OpenApiDocumentTransformerContext _, CancellationToken __)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();

        AddNamedEnumSchema(document, "ProfileFieldAction", "Progressive field action. absent is only valid for PUT replace semantics.", FieldActions);
        AddNamedEnumSchema(document, "ProfileFieldDurability", "Only durable mutations are accepted on profile endpoints.", FieldDurabilities);
        AddNamedEnumSchema(document, "PreferenceCommandAction", "Explicit preference or restriction command action.", PreferenceActions);
        AddCollectionResponseSchemas(document);
        WireProfileResponseSchema(document);
        WireFieldMutationEnums(document);
        WirePreferenceCommandAction(document);

        foreach (var path in new[]
                 {
                     "/api/v1/profile",
                     "/api/v1/profile/preferences",
                     "/api/v1/profile/equipment",
                     "/api/v1/profile/completeness"
                 })
        {
            if (!document.Paths.TryGetValue(path, out var pathItem) || pathItem.Operations is null)
            {
                continue;
            }

            foreach (var (method, operation) in pathItem.Operations)
            {
                if (method == HttpMethod.Put || method == HttpMethod.Patch)
                {
                    EnsureHeader(operation, "If-Match", "Required for updates after the first create. Omit on the first profile create. Use the ETag from GET when a profile already exists.", required: false);
                    EnsureHeader(operation, "X-CSRF-TOKEN", "Required CSRF token issued by GET /api/v1/session.", required: true);
                    EnsureProblem(operation, "400", "Validation failed for CSRF, action, durability, field values, or duplicate equipment codes.");
                    EnsureProblem(operation, "401", "Authentication is required.");
                    EnsureProblem(operation, "403", "The caller is not authorized for this profile.");
                    EnsureProblem(operation, "409", "A profile already exists or another controlled conflict occurred.");
                    EnsureProblem(operation, "412", "The profile version is out of date or the ETag is not bound to the authenticated owner.");
                    EnsureProblem(operation, "428", "An If-Match header is required for this update.");
                }

                if (method == HttpMethod.Get)
                {
                    EnsureProblem(operation, "401", "Authentication is required.");
                }

                if (path is "/api/v1/profile/preferences" or "/api/v1/profile/equipment")
                {
                    WireCollectionOperationSchema(document, operation, method, path);
                }

                if (path == "/api/v1/profile" && method == HttpMethod.Get)
                {
                    WireMissingProfileGetDescription(operation);
                }

                if (EmitsEtag(path, method))
                {
                    foreach (var status in new[] { "200", "201" })
                    {
                        EnsureEtagOnStatus(operation, status, path, method);
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    private static bool EmitsEtag(string path, HttpMethod method) =>
        path is "/api/v1/profile" or "/api/v1/profile/preferences" or "/api/v1/profile/equipment"
        && (method == HttpMethod.Get || method == HttpMethod.Put || method == HttpMethod.Patch);

    private static void AddNamedEnumSchema(OpenApiDocument document, string name, string description, IReadOnlyList<string> values)
    {
        document.Components!.Schemas![name] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Enum = values.Select(value => (JsonNode)JsonValue.Create(value)!).ToList(),
            Description = description
        };
    }

    private static void AddCollectionResponseSchemas(OpenApiDocument document)
    {
        EnsureCollectionResponseSchema(document, "PreferencesCollectionResponse", "PreferenceResponse");
        EnsureCollectionResponseSchema(document, "EquipmentCollectionResponse", "EquipmentResponse");
    }

    private static void EnsureCollectionResponseSchema(OpenApiDocument document, string schemaName, string entrySchemaName)
    {
        if (document.Components!.Schemas!.TryGetValue(schemaName, out var existing) && existing is OpenApiSchema typed)
        {
            typed.Properties ??= new Dictionary<string, IOpenApiSchema>();
            typed.Properties["version"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String | JsonSchemaType.Null,
                Description = "Opaque profile version matching the ETag header when a profile exists; null when no profile exists yet."
            };
            return;
        }

        document.Components.Schemas[schemaName] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string> { "entries" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["version"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String | JsonSchemaType.Null,
                    Description = "Opaque profile version matching the ETag header when a profile exists; null when no profile exists yet."
                },
                ["entries"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = new OpenApiSchemaReference(entrySchemaName, document)
                }
            }
        };
    }

    private static void WireProfileResponseSchema(OpenApiDocument document)
    {
        if (!document.Components!.Schemas!.TryGetValue("ProfileResponse", out var schema) || schema is not OpenApiSchema typed)
        {
            return;
        }

        typed.Properties ??= new Dictionary<string, IOpenApiSchema>();
        typed.Properties["profileExists"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Boolean,
            Description = "True when a durable profile row is persisted for the authenticated owner."
        };
        typed.Properties["version"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String | JsonSchemaType.Null,
            Description = "Opaque profile version matching the ETag header when a profile exists; null when no profile exists yet."
        };
        typed.Properties["createdAt"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String | JsonSchemaType.Null,
            Format = "date-time",
            Description = "Persisted profile creation timestamp; null when profileExists is false."
        };
        typed.Properties["updatedAt"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String | JsonSchemaType.Null,
            Format = "date-time",
            Description = "Persisted profile last-update timestamp; null when profileExists is false."
        };
        typed.Required ??= new HashSet<string>(StringComparer.Ordinal);
        typed.Required.Add("profileExists");
    }

    private static void WireMissingProfileGetDescription(OpenApiOperation operation)
    {
        operation.Responses ??= new OpenApiResponses();
        if (!operation.Responses.TryGetValue("200", out var response) || response is not OpenApiResponse typed)
        {
            return;
        }

        typed.Description = "Progressive profile projection. profileExists is false, version/createdAt/updatedAt are null, and ETag is omitted when no profile has been persisted yet.";
    }

    private static void WireFieldMutationEnums(OpenApiDocument document)
    {
        foreach (var schemaName in document.Components!.Schemas!.Keys.Where(name => name.StartsWith("FieldMutationDto", StringComparison.Ordinal)).ToList())
        {
            if (document.Components.Schemas[schemaName] is not OpenApiSchema schema || schema.Properties is null)
            {
                continue;
            }

            schema.Properties["action"] = new OpenApiSchemaReference("ProfileFieldAction", document);
            schema.Properties["durability"] = new OpenApiSchemaReference("ProfileFieldDurability", document);
        }
    }

    private static void WirePreferenceCommandAction(OpenApiDocument document)
    {
        if (document.Components!.Schemas!.TryGetValue("PreferenceCommandDto", out var schema)
            && schema is OpenApiSchema typed
            && typed.Properties is not null)
        {
            typed.Properties["action"] = new OpenApiSchemaReference("PreferenceCommandAction", document);
        }
    }

    private static void WireCollectionOperationSchema(OpenApiDocument document, OpenApiOperation operation, HttpMethod method, string path)
    {
        var schemaName = path.EndsWith("/preferences", StringComparison.Ordinal)
            ? "PreferencesCollectionResponse"
            : "EquipmentCollectionResponse";
        operation.Responses ??= new OpenApiResponses();
        if (!operation.Responses.TryGetValue("200", out var response) || response is not OpenApiResponse typed)
        {
            typed = new OpenApiResponse { Description = "OK" };
            operation.Responses["200"] = typed;
        }

        typed.Content ??= new Dictionary<string, OpenApiMediaType>();
        typed.Content["application/json"] = new OpenApiMediaType
        {
            Schema = new OpenApiSchemaReference(schemaName, document)
        };

        if (method == HttpMethod.Get)
        {
            typed.Description = "Collection returned. version is null and ETag is omitted when no profile exists yet.";
        }
    }

    private static void EnsureHeader(OpenApiOperation operation, string name, string description, bool required)
    {
        operation.Parameters ??= [];
        if (operation.Parameters.Any(parameter => parameter is OpenApiParameter typed && typed.Name == name && typed.In == ParameterLocation.Header))
        {
            return;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = required,
            Description = description,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        });
    }

    private static void EnsureProblem(OpenApiOperation operation, string status, string description)
    {
        operation.Responses ??= new OpenApiResponses();
        if (operation.Responses.ContainsKey(status))
        {
            return;
        }

        operation.Responses[status] = new OpenApiResponse { Description = description };
    }

    private static void EnsureEtagOnStatus(OpenApiOperation operation, string status, string path, HttpMethod method)
    {
        operation.Responses ??= new OpenApiResponses();
        if (!operation.Responses.TryGetValue(status, out var response) || response is not OpenApiResponse typed)
        {
            return;
        }

        var description = path == "/api/v1/profile" && method == HttpMethod.Get
            ? "Opaque profile version token. Omitted when profileExists is false."
            : "Opaque profile version token. Omitted for preference/equipment collections when no profile exists.";

        typed.Headers ??= new Dictionary<string, IOpenApiHeader>();
        typed.Headers["ETag"] = new OpenApiHeader
        {
            Description = description,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        };
    }
}
