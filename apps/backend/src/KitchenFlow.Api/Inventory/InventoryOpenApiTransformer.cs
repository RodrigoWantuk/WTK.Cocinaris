using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace KitchenFlow.Api.Inventory;

internal static class InventoryOpenApiTransformer
{
    internal static Task ApplyAsync(OpenApiDocument document, OpenApiDocumentTransformerContext _, CancellationToken __)
    {
        // The emitted contract must not drift merely because CI uses an HTTP loopback listener
        // while local cookie development uses HTTPS. Consumers configure their actual base URL.
        document.Servers = [new OpenApiServer { Url = "/", Description = "Current KitchenFlow backend origin. Local development defaults to https://localhost:7443." }];
        document.Info.License = new OpenApiLicense
        {
            Name = "PolyForm Noncommercial License 1.0.0",
            Url = new Uri("https://polyformproject.org/licenses/noncommercial/1.0.0/")
        };
        foreach (var tag in document.Tags ?? new HashSet<OpenApiTag>())
        {
            tag.Description ??= "KitchenFlow authenticated backend and health operations.";
        }
        var components = document.Components ??= new OpenApiComponents();
        (components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>())["kitchenflowSession"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Cookie,
            Name = "__Host-kitchenflow-session",
            Description = "Backend-managed, secure HttpOnly session cookie. Browser code must not read or store OIDC tokens."
        };

        if (components.Schemas is { } schemas && schemas.TryGetValue("ProblemDetails", out var problemSchema) && problemSchema is OpenApiSchema problemDetails)
        {
            (problemDetails.Properties ??= new Dictionary<string, IOpenApiSchema>())["errorCode"] = new OpenApiSchema
            {
                Description = "Stable machine-readable KitchenFlow error code.",
                Type = JsonSchemaType.String
            };
            problemDetails.Properties["traceId"] = new OpenApiSchema
            {
                Description = "Correlation identifier suitable for support without exposing private request data.",
                Type = JsonSchemaType.String
            };
            problemDetails.Properties["errors"] = new OpenApiSchema
            {
                Description = "Field-level validation messages keyed by request field name.",
                Type = JsonSchemaType.Object,
                AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.String } }
            };
            problemDetails.Required ??= new HashSet<string>(StringComparer.Ordinal);
            problemDetails.Required.Add("errorCode");
            problemDetails.Required.Add("traceId");
            if (problemDetails.Properties.TryGetValue("status", out var statusProperty) && statusProperty is OpenApiSchema statusSchema)
            {
                statusSchema.Type = JsonSchemaType.Integer;
                statusSchema.Format = "int32";
            }
        }

        ConfigureDecimal(components, "QuantityRequest", "measuredValue", true);
        ConfigureDecimal(components, "QuantityResponse", "measuredValue", true);
        ConfigureDecimal(components, "AdjustmentRequest", "value", true);
        ConfigureStringEnum(components, "QuantityRequest", "unit", true, "Gram", "Milliliter", "Unit");
        ConfigureStringEnum(components, "QuantityResponse", "unit", true, "Gram", "Milliliter", "Unit");
        ConfigureStringEnum(components, "QuantityRequest", "availabilityState", true, "Available", "Low", "Unavailable");
        ConfigureStringEnum(components, "QuantityResponse", "availabilityState", true, "Available", "Low", "Unavailable");
        ConfigureStringEnum(components, "CreateLotRequest", "storageLocation", false, "Pantry", "Refrigerator", "Freezer", "Other");
        ConfigureStringEnum(components, "UpdateLotRequest", "storageLocation", false, "Pantry", "Refrigerator", "Freezer", "Other");
        ConfigureStringEnum(components, "CreateLotRequest", "packageState", true, "Sealed", "Opened", "Unknown");
        ConfigureStringEnum(components, "UpdateLotRequest", "packageState", true, "Sealed", "Opened", "Unknown");
        ConfigureStringEnum(components, "AdjustmentRequest", "type", false, "Consume", "Discard", "Correct", "AvailabilityChanged");
        ConfigureQuantityModeSchema(components, "QuantityRequest");
        ConfigureQuantityModeSchema(components, "QuantityResponse");

        foreach (var (path, pathItem) in document.Paths)
        {
            if (!path.StartsWith("/api/v1/", StringComparison.Ordinal))
            {
                foreach (var operation in pathItem.Operations is { } anonymousOperations ? anonymousOperations.Values.AsEnumerable() : Enumerable.Empty<OpenApiOperation>())
                {
                    operation.Security = [];
                    operation.Summary ??= $"{operation.Description ?? "KitchenFlow health operation"}.";
                    operation.OperationId ??= $"health{path.Replace("/", string.Empty, StringComparison.Ordinal)}";
                }
                continue;
            }

            foreach (var (method, operation) in pathItem.Operations ?? [])
            {
                operation.Summary ??= $"{method.Method} {path}";
                operation.OperationId ??= CreateOperationId(method, path);
                if (path.StartsWith("/api/v1/auth/login", StringComparison.Ordinal))
                {
                    operation.Security = [];
                }
                else
                {
                    operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("kitchenflowSession", document)] = [] }];
                    EnsureProblemResponse(document, operation, "401", "Authentication is required.");
                    EnsureProblemResponse(document, operation, "500", "An unexpected server error occurred.");
                }

                var isLoginChallenge = path.Equals("/api/v1/auth/login", StringComparison.Ordinal) && method == HttpMethod.Post;
                var stateChanging = !isLoginChallenge && (method == HttpMethod.Post || method == HttpMethod.Patch || method == HttpMethod.Delete);
                if (stateChanging)
                {
                    operation.Parameters ??= [];
                    operation.Parameters.Add(Header("X-CSRF-TOKEN", "Required CSRF token issued by GET /api/v1/session."));
                    EnsureProblemResponse(document, operation, "400", "The request or CSRF token is invalid.");
                }

                if (operation.RequestBody is not null)
                {
                    EnsureProblemResponse(document, operation, "415", "The request content type is not supported.");
                }

                if (stateChanging && path.Contains("/inventory/", StringComparison.Ordinal))
                {
                    EnsureProblemResponse(document, operation, "429", "The mutation rate limit was exceeded.");
                }

                if ((path.Contains("/lots", StringComparison.Ordinal) && (method == HttpMethod.Patch || method == HttpMethod.Delete)) || path.EndsWith("/adjustments", StringComparison.Ordinal))
                {
                    operation.Parameters ??= [];
                    operation.Parameters.Add(Header("If-Match", "Required opaque concurrency ETag from the current lot representation."));
                }

                if ((path.EndsWith("/lots", StringComparison.Ordinal) && method == HttpMethod.Post) || path.EndsWith("/adjustments", StringComparison.Ordinal))
                {
                    operation.Parameters ??= [];
                    operation.Parameters.Add(Header("Idempotency-Key", "Required UUID key for semantic create or adjustment replay."));
                }

                if ((path.Equals("/api/v1/recipes/generation-sessions", StringComparison.Ordinal) && method == HttpMethod.Post)
                    || (path.Equals("/api/v1/recipes/generation-sessions/{sessionId}/selection", StringComparison.Ordinal) && method == HttpMethod.Post))
                {
                    operation.Parameters ??= [];
                    operation.Parameters.Add(Header("Idempotency-Key", "Required UUID key for cook-now generation or selection replay."));
                    EnsureProblemResponse(document, operation, "429", "The mutation rate limit was exceeded.");
                    EnsureProblemResponse(document, operation, "503", "The AI Gateway is temporarily unavailable.");
                }

                AddExamples(path, method, operation);
                AddStandardProblemExamples(operation);

                var emitsLotEtag = path.EndsWith("/lots", StringComparison.Ordinal) && method == HttpMethod.Post ||
                    path.EndsWith("/lots/{lotId}", StringComparison.Ordinal) && (method == HttpMethod.Get || method == HttpMethod.Patch) ||
                    path.EndsWith("/adjustments", StringComparison.Ordinal) && method == HttpMethod.Post;
                foreach (var response in emitsLotEtag ? (operation.Responses ?? []).Where(response => response.Key is "200" or "201") : [])
                {
                    if (response.Value is OpenApiResponse typedResponse)
                    {
                        (typedResponse.Headers ??= new Dictionary<string, IOpenApiHeader>())["ETag"] = new OpenApiHeader { Description = "Opaque current version required by If-Match on subsequent mutations.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } };
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    private static OpenApiParameter Header(string name, string description) => new()
    {
        Name = name,
        In = ParameterLocation.Header,
        Required = true,
        Description = description,
        Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = name == "Idempotency-Key" ? "uuid" : null }
    };

    private static void ConfigureDecimal(OpenApiComponents components, string schemaName, string propertyName, bool nullable)
    {
        if (TryGetProperty(components, schemaName, propertyName, out var property))
        {
            property.Type = nullable ? JsonSchemaType.Number | JsonSchemaType.Null : JsonSchemaType.Number;
            property.Format = "decimal";
        }
    }

    private static void ConfigureStringEnum(OpenApiComponents components, string schemaName, string propertyName, bool nullable, params string[] values)
    {
        if (TryGetProperty(components, schemaName, propertyName, out var property))
        {
            var enumValues = values.Select(value => (JsonNode)JsonValue.Create(value)!).ToList();
            if (nullable)
            {
                property.Type = null;
                property.Enum = null;
                property.AnyOf =
                [
                    new OpenApiSchema { Type = JsonSchemaType.String, Enum = enumValues },
                    new OpenApiSchema { Type = JsonSchemaType.Null }
                ];
            }
            else
            {
                property.Type = JsonSchemaType.String;
                property.Enum = enumValues;
            }
        }
    }

    private static bool TryGetProperty(OpenApiComponents components, string schemaName, string propertyName, out OpenApiSchema property)
    {
        property = null!;
        return components.Schemas is { } schemas && schemas.TryGetValue(schemaName, out var schema) && schema is OpenApiSchema typedSchema && typedSchema.Properties is { } properties && properties.TryGetValue(propertyName, out var candidate) && candidate is OpenApiSchema typedProperty && (property = typedProperty) is not null;
    }

    private static void ConfigureQuantityModeSchema(OpenApiComponents components, string schemaName)
    {
        if (components.Schemas is not { } schemas || !schemas.TryGetValue(schemaName, out var schema) || schema is not OpenApiSchema quantitySchema)
        {
            return;
        }

        quantitySchema.Required = new HashSet<string>(StringComparer.Ordinal);
        quantitySchema.OneOf =
        [
            new OpenApiSchema
            {
                Title = "Measured quantity",
                Required = new HashSet<string>(StringComparer.Ordinal) { "measuredValue", "unit" },
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["availabilityState"] = new OpenApiSchema { Type = JsonSchemaType.Null }
                }
            },
            new OpenApiSchema
            {
                Title = "Availability quantity",
                Required = new HashSet<string>(StringComparer.Ordinal) { "availabilityState" },
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["measuredValue"] = new OpenApiSchema { Type = JsonSchemaType.Null },
                    ["unit"] = new OpenApiSchema { Type = JsonSchemaType.Null }
                }
            }
        ];
    }

    private static void AddExamples(string path, HttpMethod method, OpenApiOperation operation)
    {
        if (path.EndsWith("/lots", StringComparison.Ordinal) && method == HttpMethod.Post)
        {
            AddRequestExample(operation, "measuredLot", "A manually entered measured pantry lot.", """{"productName":"Red lentils","quantity":{"measuredValue":500,"unit":"Gram","availabilityState":null},"storageLocation":"Pantry","customLocation":null,"packageState":"Sealed","printedExpirationDate":"2026-12-31","notes":null}""");
            AddRequestExample(operation, "availabilityLot", "A manually entered qualitative availability lot.", """{"productName":"Fresh herbs","quantity":{"measuredValue":null,"unit":null,"availabilityState":"Low"},"storageLocation":"Refrigerator","customLocation":null,"packageState":null,"printedExpirationDate":null,"notes":null}""");
            AddResponseExample(operation, "201", "completedCreate", "A newly completed create command.", LotExample("opaque-create-version"));
            AddResponseExample(operation, "201", "completedCreateReplay", "The exact semantic response returned for a completed create replay.", LotExample("opaque-create-version"));
            AddProblemExample(operation, "400", "fieldValidationFailure", "A required header or request field is malformed.", ProblemExample(400, "validation_failed", "A UUID Idempotency-Key header is required.", """{"Idempotency-Key":["A UUID Idempotency-Key header is required."]}"""));
            AddProblemExample(operation, "422", "invalidQuantity", "A measured and availability quantity cannot be supplied together.", ProblemExample(422, "domain_rule_violated", "Quantity must use exactly one mode.", """{"quantity":["Quantity must use exactly one mode."]}"""));
        }

        if (path.EndsWith("/adjustments", StringComparison.Ordinal) && method == HttpMethod.Post)
        {
            AddRequestExample(operation, "consume", "Consumes a measured quantity from the current lot version.", """{"type":"Consume","value":125,"availabilityState":null,"reasonCode":"meal","note":null}""");
            AddResponseExample(operation, "200", "completedAdjustmentReplay", "The exact semantic response returned for a completed adjustment replay.", LotExample("opaque-adjusted-version", 375));
            AddProblemExample(operation, "412", "staleEtag", "The supplied opaque ETag is no longer current.", ProblemExample(412, "precondition_failed", "The inventory lot was modified."));
            AddProblemExample(operation, "409", "reusedIdempotencyKey", "The key was used for a different semantic command.", ProblemExample(409, "idempotency_key_reused", "The Idempotency-Key was used for a different request."));
            AddProblemExample(operation, "422", "domainRuleFailure", "The adjustment violates an inventory rule.", ProblemExample(422, "domain_rule_violated", "The adjustment exceeds the current quantity.", """{"value":["The adjustment exceeds the current quantity."]}"""));
        }

        if (path.EndsWith("/lots", StringComparison.Ordinal) && method == HttpMethod.Get)
        {
            AddProblemExample(operation, "400", "invalidCursor", "The cursor is invalid or has been tampered with.", ProblemExample(400, "invalid_cursor", "The cursor is invalid."));
        }

        if ((method == HttpMethod.Patch || method == HttpMethod.Delete || path.EndsWith("/adjustments", StringComparison.Ordinal)) && path.Contains("/lots/", StringComparison.Ordinal))
        {
            AddProblemExample(operation, "428", "missingPrecondition", "The mutation requires If-Match.", ProblemExample(428, "precondition_required", "If-Match is required."));
        }
    }

    private static void AddStandardProblemExamples(OpenApiOperation operation)
    {
        AddProblemExample(operation, "401", "authenticationFailure", "The backend-managed session is absent or invalid.", ProblemExample(401, "authentication_required", "Authentication is required."));
        AddProblemExample(operation, "415", "unsupportedMediaType", "The request content type is unsupported.", ProblemExample(415, "unsupported_media_type", "The request content type is not supported."));
        AddProblemExample(operation, "429", "rateLimitFailure", "The bounded mutation rate was exceeded.", ProblemExample(429, "rate_limit_exceeded", "The request rate limit was exceeded."));
        AddProblemExample(operation, "500", "unexpectedFailure", "An unexpected error was safely redacted.", ProblemExample(500, "unexpected_error", "An unexpected error occurred."));
    }

    private static void EnsureProblemResponse(OpenApiDocument document, OpenApiOperation operation, string statusCode, string description)
    {
        operation.Responses ??= new OpenApiResponses();
        if (operation.Responses.ContainsKey(statusCode))
        {
            return;
        }

        operation.Responses[statusCode] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new() { Schema = new OpenApiSchemaReference("ProblemDetails", document) }
            }
        };
    }

    private static string ProblemExample(int status, string errorCode, string detail, string? errors = null) =>
        $$"""{"type":"about:blank","title":"{{ReasonPhrase(status)}}","status":{{status}},"detail":{{JsonSerializer.Serialize(detail)}},"errorCode":"{{errorCode}}","traceId":"00-00000000000000000000000000000000-0000000000000000-00"{{(errors is null ? string.Empty : $"," + "\"errors\":" + errors)}}}""";

    private static string LotExample(string version, decimal measuredValue = 500) =>
        $$"""{"lotId":"00000000-0000-0000-0000-000000000001","productId":"00000000-0000-0000-0000-000000000002","productName":"Red lentils","quantity":{"measuredValue":{{measuredValue.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"unit":"Gram","availabilityState":null},"storageLocation":"Pantry","customLocation":null,"packageState":"Sealed","printedExpirationDate":"2026-12-31","notes":null,"version":"{{version}}","createdAt":"2026-07-31T00:00:00Z","updatedAt":"2026-07-31T00:00:00Z"}""";

    private static string ReasonPhrase(int status) => status switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        409 => "Conflict",
        412 => "Precondition Failed",
        415 => "Unsupported Media Type",
        422 => "Unprocessable Entity",
        428 => "Precondition Required",
        429 => "Too Many Requests",
        _ => "Internal Server Error"
    };

    private static string CreateOperationId(HttpMethod method, string path) => method.Method.ToLowerInvariant() + string.Concat(path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(segment => ToPascalCaseSegment(segment)));

    private static string ToPascalCaseSegment(string segment)
    {
        var normalized = segment.Trim('{', '}');
        return char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }

    private static void AddRequestExample(OpenApiOperation operation, string name, string summary, string value)
    {
        if (operation.RequestBody?.Content is { } content && content.TryGetValue("application/json", out var requestMediaType) && requestMediaType is OpenApiMediaType mediaType)
        {
            (mediaType.Examples ??= new Dictionary<string, IOpenApiExample>())[name] = new OpenApiExample { Summary = summary, Value = JsonNode.Parse(value) };
        }
    }

    private static void AddProblemExample(OpenApiOperation operation, string statusCode, string name, string summary, string value)
    {
        if (operation.Responses is { } responses && responses.TryGetValue(statusCode, out var response) && response.Content is { } content && content.TryGetValue("application/problem+json", out var responseMediaType) && responseMediaType is OpenApiMediaType mediaType)
        {
            (mediaType.Examples ??= new Dictionary<string, IOpenApiExample>())[name] = new OpenApiExample { Summary = summary, Value = JsonNode.Parse(value) };
        }
    }

    private static void AddResponseExample(OpenApiOperation operation, string statusCode, string name, string summary, string value)
    {
        if (operation.Responses is { } responses && responses.TryGetValue(statusCode, out var response) && response.Content is { } content && content.TryGetValue("application/json", out var responseMediaType) && responseMediaType is OpenApiMediaType mediaType)
        {
            (mediaType.Examples ??= new Dictionary<string, IOpenApiExample>())[name] = new OpenApiExample { Summary = summary, Value = JsonNode.Parse(value) };
        }
    }
}
