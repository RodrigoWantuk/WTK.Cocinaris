using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Text.Json;
using Cocinaris.Api.Observability;
using Cocinaris.Api.Services;
using Cocinaris.Modules.Inventory.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Cocinaris.IntegrationTests;

public sealed class TelemetryRedactionTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("/inventory/lots", "/inventory/lots")]
    [InlineData("https://attacker.example", "/")]
    [InlineData("//attacker.example", "/")]
    [InlineData("/\\attacker.example", "/")]
    [InlineData("/%2f%2fattacker.example", "/")]
    [InlineData("/%252f%252fattacker.example", "/")]
    [InlineData("/safe%5c%5cattacker.example", "/")]
    [InlineData("/signin-oidc", "/")]
    [InlineData("/signout-callback-oidc", "/")]
    [InlineData("/api/v1/auth/logout", "/")]
    [InlineData("/bad%zz", "/")]
    [InlineData("/safe/../api/v1/auth/login", "/")]
    [InlineData("/inventory/../api/v1/auth/logout", "/")]
    [InlineData("/foo/./bar/../inventory/lots", "/foo/inventory/lots")]
    public void ReturnUrlPolicyAllowsOnlyLocalPaths(string? candidate, string expected)
    {
        Assert.Equal(expected, ReturnUrlPolicy.Normalize(candidate));
    }

    [Fact]
    public void RuntimeConfigurationReadinessRejectsProductionPlaceholderAndAcceptsValidConfiguration()
    {
        var session = new SessionOptions();
        var idempotency = new IdempotencyOptions { Retention = TimeSpan.FromDays(30) };
        var rejected = new RuntimeConfigurationReadiness(
            false,
            new DatabaseOptions { ConnectionString = "Host=database;Password=valid-secret-value" },
            new OidcOptions { Authority = "https://identity.example/realms/cocinaris", ClientId = "cocinaris", ClientSecret = "development-only-change-me" },
            new DataProtectionOptions { KeyRingPath = "/var/lib/cocinaris/keys" },
            session,
            idempotency);
        var accepted = new RuntimeConfigurationReadiness(
            false,
            new DatabaseOptions { ConnectionString = "Host=database;Password=valid-secret-value" },
            new OidcOptions { Authority = "https://identity.example/realms/cocinaris", ClientId = "cocinaris", ClientSecret = "valid-secret-value" },
            new DataProtectionOptions { KeyRingPath = "/var/lib/cocinaris/keys" },
            session,
            idempotency);
        var development = new RuntimeConfigurationReadiness(
            true,
            new DatabaseOptions { ConnectionString = "Host=database;Password=development-only-change-me" },
            new OidcOptions { Authority = "http://127.0.0.1:8080/realms/cocinaris", ClientId = "cocinaris" },
            new DataProtectionOptions(),
            session,
            idempotency);

        Assert.Throws<InvalidOperationException>(rejected.ThrowIfInvalidForNonDevelopment);
        accepted.ThrowIfInvalidForNonDevelopment();
        Assert.True(development.IsReady);
    }

    [Fact]
    public void ProductionStartupRejectsPlaceholderSecrets()
    {
        using var factory = CreateProductionFactory("development-only-change-me", "/var/lib/cocinaris/keys");

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("configuration", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("development-only-change-me", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionStartupAcceptsCompleteTypedConfiguration()
    {
        var keyRingPath = Path.Combine(Path.GetTempPath(), $"cocinaris-keyring-{Guid.NewGuid():N}");
        using var factory = CreateProductionFactory("valid-secret-value", keyRingPath);
        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(90, true)]
    [InlineData(91, false)]
    public void IdempotencyRetentionIsBoundedToTheDocumentedReplayWindow(int days, bool expected)
    {
        var options = new IdempotencyOptions { Retention = TimeSpan.FromDays(days) };

        Assert.Equal(expected, options.IsValid());
    }

    [Fact]
    public void SensitiveTelemetryTagsAreRemovedBeforeExport()
    {
        using var activity = new Activity("inventory.mutation");
        activity.Start();
        activity.SetTag("http.request.header.authorization", "Bearer private-token");
        activity.SetTag("http.request.header.cookie", "session=private-cookie");
        activity.SetTag("inventory.product.name", "private product");
        activity.SetTag("inventory.note", "private note");
        activity.SetTag("http.request.body", "private request body");
        activity.SetTag("http.response.status_code", 200);

        SensitiveTelemetryRedactionProcessor.Redact(activity);

        Assert.DoesNotContain(activity.TagObjects, tag => tag.Key.Contains("authorization", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(activity.TagObjects, tag => tag.Key.Contains("cookie", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(activity.TagObjects, tag => tag.Key.Contains("product", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(activity.TagObjects, tag => tag.Key.Contains("note", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(activity.TagObjects, tag => tag.Key.Contains("body", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(activity.TagObjects, tag => tag.Key == "http.response.status_code" && (int)tag.Value! == 200);
    }

    [Fact]
    public void TraceExporterReceivesOnlyRedactedTagsAfterFullProcessorPipeline()
    {
        var exported = new List<Activity>();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource("Cocinaris.TelemetryTest")
            .AddProcessor(new SensitiveTelemetryRedactionProcessor())
            .AddProcessor(new SimpleActivityExportProcessor(new CapturingActivityExporter(exported)))
            .Build();
        using var source = new ActivitySource("Cocinaris.TelemetryTest");
        using (var activity = source.StartActivity("inventory.adjust"))
        {
            Assert.NotNull(activity);
            activity.SetTag("http.request.header.authorization", "Bearer private-token");
            activity.SetTag("http.request.header.cookie", "session=private-cookie");
            activity.SetTag("inventory.product.name", "private product");
            activity.SetTag("inventory.note", "private note");
            activity.SetTag("http.request.body", "private body");
            activity.SetTag("http.response.status_code", 200);
        }

        Assert.True(provider.ForceFlush());
        var completed = Assert.Single(exported);
        Assert.DoesNotContain(completed.TagObjects, tag => tag.Key.Contains("authorization", StringComparison.OrdinalIgnoreCase) || tag.Key.Contains("cookie", StringComparison.OrdinalIgnoreCase) || tag.Key.Contains("product", StringComparison.OrdinalIgnoreCase) || tag.Key.Contains("note", StringComparison.OrdinalIgnoreCase) || tag.Key.Contains("body", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(completed.TagObjects, tag => tag.Key == "http.response.status_code" && (int)tag.Value! == 200);
    }

    [Fact]
    public void InventoryMetricsUseOnlyStableLowCardinalityTags()
    {
        var observed = new List<(string Instrument, IReadOnlyDictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Cocinaris.Inventory")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            var capturedTags = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                capturedTags[tag.Key] = tag.Value;
            }

            observed.Add((instrument.Name, capturedTags));
        });
        listener.Start();

        var metrics = new InventoryMetrics();
        metrics.RecordMutation("adjust", InventoryApplicationResult<InventoryLotView>.Failure("domain_rule_violated", "A private product and note must not become metric tags."));

        Assert.Contains(observed, measurement => measurement.Instrument == "cocinaris.inventory.mutations" && measurement.Tags["operation"]?.ToString() == "adjust");
        Assert.Contains(observed, measurement => measurement.Instrument == "cocinaris.inventory.rejections" && measurement.Tags["category"]?.ToString() == "domain_rule_violated");
        Assert.DoesNotContain(observed.SelectMany(measurement => measurement.Tags), tag => tag.Key.Contains("product", StringComparison.OrdinalIgnoreCase) || tag.Key.Contains("note", StringComparison.OrdinalIgnoreCase) || tag.Value?.ToString()?.Contains("private", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void MetricExporterReceivesOnlyRegisteredNamesLabelsAndBoundedOutcomes()
    {
        var exporter = new CapturingMetricExporter();
        using var provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter("Cocinaris.Inventory")
            .AddMeter("Cocinaris.Security")
            .AddReader(new PeriodicExportingMetricReader(exporter))
            .Build();
        var inventory = new InventoryMetrics();
        var security = new SecurityMetrics();

        inventory.RecordMutation("create", InventoryApplicationResult<object>.Succeeded(new object(), InventoryApplicationSuccess.Created, InventoryIdempotencyDisposition.Completed));
        inventory.RecordMutation("adjust", InventoryApplicationResult<object>.Succeeded(new object(), idempotency: InventoryIdempotencyDisposition.Replayed));
        inventory.RecordMutation("adjust", InventoryApplicationResult<object>.Failure("idempotency_key_reused", "private detail"));
        inventory.RecordMutation("adjust", InventoryApplicationResult<object>.Failure("idempotency_in_progress", "private detail"));
        security.RecordFailure("authentication");
        security.RecordFailure("authorization");
        security.RecordFailure("csrf");
        security.RecordFailure("rate_limit");

        Assert.True(provider.ForceFlush());
        Assert.Contains(exporter.Measurements, measurement => measurement.Name == "cocinaris.inventory.idempotency" && measurement.Tags["outcome"] == "completed");
        Assert.Contains(exporter.Measurements, measurement => measurement.Name == "cocinaris.inventory.idempotency" && measurement.Tags["outcome"] == "replayed");
        Assert.Contains(exporter.Measurements, measurement => measurement.Name == "cocinaris.inventory.idempotency" && measurement.Tags["outcome"] == "reused");
        Assert.Contains(exporter.Measurements, measurement => measurement.Name == "cocinaris.inventory.idempotency" && measurement.Tags["outcome"] == "in_progress");
        Assert.Equal(["authentication", "authorization", "csrf", "rate_limit"], exporter.Measurements.Where(measurement => measurement.Name == "cocinaris.security.access_failures").Select(measurement => measurement.Tags["category"]).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray());
        Assert.All(exporter.Measurements.SelectMany(measurement => measurement.Tags), tag => Assert.Contains(tag.Key, new[] { "operation", "outcome", "category" }));
        Assert.DoesNotContain(exporter.Measurements.SelectMany(measurement => measurement.Tags), tag => tag.Value.Contains("private", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GeneratedOpenApiDeclaresInventorySuccessAndProblemResponses()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:Cocinaris", "Host=127.0.0.1;Database=cocinaris_contract_test;Username=test;Password=test"));
        using var client = factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var createResponses = document.GetProperty("paths").GetProperty("/api/v1/inventory/lots").GetProperty("post").GetProperty("responses");
        var updateResponses = document.GetProperty("paths").GetProperty("/api/v1/inventory/lots/{lotId}").GetProperty("patch").GetProperty("responses");

        Assert.True(createResponses.TryGetProperty("201", out _));
        Assert.True(createResponses.TryGetProperty("422", out _));
        Assert.True(updateResponses.TryGetProperty("428", out _));
        Assert.True(updateResponses.TryGetProperty("412", out _));

        var securityScheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("cocinarisSession");
        var createParameters = document.GetProperty("paths").GetProperty("/api/v1/inventory/lots").GetProperty("post").GetProperty("parameters");
        var sessionResponses = document.GetProperty("paths").GetProperty("/api/v1/session").GetProperty("get").GetProperty("responses");
        var loginParameters = document.GetProperty("paths").GetProperty("/api/v1/auth/login").GetProperty("post").TryGetProperty("parameters", out var loginParameterValue) ? loginParameterValue : default;
        var createResponseHeaders = createResponses.GetProperty("201").GetProperty("headers");
        var sessionResponse = sessionResponses.GetProperty("200");
        var historySchema = document.GetProperty("components").GetProperty("schemas").GetProperty("LotHistoryResponse").GetProperty("properties");

        Assert.Equal("cookie", securityScheme.GetProperty("in").GetString());
        Assert.Contains(createParameters.EnumerateArray(), parameter => parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN");
        Assert.Contains(createParameters.EnumerateArray(), parameter => parameter.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.True(sessionResponses.GetProperty("200").GetProperty("content").GetProperty("application/json").TryGetProperty("schema", out _));
        Assert.Contains(createResponseHeaders.EnumerateObject(), header => header.Name == "ETag");
        Assert.False(sessionResponse.TryGetProperty("headers", out var sessionHeaders) && sessionHeaders.TryGetProperty("ETag", out _));
        Assert.False(loginParameters.ValueKind == JsonValueKind.Array && loginParameters.EnumerateArray().Any(parameter => parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN"));
        Assert.True(historySchema.TryGetProperty("kind", out _));
        Assert.True(historySchema.TryGetProperty("changedFields", out _));

        var schemas = document.GetProperty("components").GetProperty("schemas");
        var measuredValue = schemas.GetProperty("QuantityRequest").GetProperty("properties").GetProperty("measuredValue");
        var quantityUnit = schemas.GetProperty("QuantityRequest").GetProperty("properties").GetProperty("unit");
        var adjustmentType = schemas.GetProperty("AdjustmentRequest").GetProperty("properties").GetProperty("type");

        Assert.Contains("number", SchemaTypes(measuredValue));
        Assert.Contains("null", SchemaTypes(measuredValue));
        Assert.Equal("decimal", measuredValue.GetProperty("format").GetString());
        Assert.Contains("Gram", EnumValues(quantityUnit));
        Assert.Contains(adjustmentType.GetProperty("enum").EnumerateArray(), value => value.GetString() == "AvailabilityChanged");

        var problemProperties = schemas.GetProperty("ProblemDetails").GetProperty("properties");
        Assert.True(problemProperties.TryGetProperty("errorCode", out _));
        Assert.True(problemProperties.TryGetProperty("traceId", out _));
        Assert.True(problemProperties.TryGetProperty("errors", out _));
        Assert.Equal(["integer"], SchemaTypes(problemProperties.GetProperty("status")));
        var errorItems = problemProperties.GetProperty("errors").GetProperty("additionalProperties").GetProperty("items");
        Assert.Equal(["string"], SchemaTypes(errorItems));
    }

    [Fact]
    public async Task GeneratedOpenApiMatchesSecurityConcurrencyNullabilityAndExampleMatrix()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:Cocinaris", "Host=127.0.0.1;Database=cocinaris_contract_test;Username=test;Password=test"));
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");
        var routes = new[]
        {
            (Path: "/api/v1/inventory/lots", Method: "get", Csrf: false, Idempotency: false, IfMatch: false, EtagStatus: (string?)null),
            (Path: "/api/v1/inventory/lots", Method: "post", Csrf: true, Idempotency: true, IfMatch: false, EtagStatus: "201"),
            (Path: "/api/v1/inventory/lots/{lotId}", Method: "get", Csrf: false, Idempotency: false, IfMatch: false, EtagStatus: "200"),
            (Path: "/api/v1/inventory/lots/{lotId}", Method: "patch", Csrf: true, Idempotency: false, IfMatch: true, EtagStatus: "200"),
            (Path: "/api/v1/inventory/lots/{lotId}", Method: "delete", Csrf: true, Idempotency: false, IfMatch: true, EtagStatus: (string?)null),
            (Path: "/api/v1/inventory/lots/{lotId}/adjustments", Method: "post", Csrf: true, Idempotency: true, IfMatch: true, EtagStatus: "200"),
            (Path: "/api/v1/inventory/lots/{lotId}/history", Method: "get", Csrf: false, Idempotency: false, IfMatch: false, EtagStatus: (string?)null)
        };

        foreach (var route in routes)
        {
            var operation = paths.GetProperty(route.Path).GetProperty(route.Method);
            Assert.Equal("cocinarisSession", operation.GetProperty("security")[0].EnumerateObject().Single().Name);
            var headers = operation.TryGetProperty("parameters", out var parameters)
                ? parameters.EnumerateArray().Where(parameter => parameter.GetProperty("in").GetString() == "header").Select(parameter => parameter.GetProperty("name").GetString()).ToHashSet(StringComparer.Ordinal)
                : [];
            Assert.Equal(route.Csrf, headers.Contains("X-CSRF-TOKEN"));
            Assert.Equal(route.Idempotency, headers.Contains("Idempotency-Key"));
            Assert.Equal(route.IfMatch, headers.Contains("If-Match"));
            Assert.True(operation.GetProperty("responses").TryGetProperty("401", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("500", out _));
            foreach (var response in operation.GetProperty("responses").EnumerateObject().Where(response => int.TryParse(response.Name, out var status) && status >= 400))
            {
                Assert.True(response.Value.GetProperty("content").TryGetProperty("application/problem+json", out _), $"{route.Method.ToUpperInvariant()} {route.Path} response {response.Name} must be Problem Details.");
            }

            if (route.EtagStatus is not null)
            {
                Assert.True(operation.GetProperty("responses").GetProperty(route.EtagStatus).GetProperty("headers").TryGetProperty("ETag", out _));
            }
        }

        var schemas = document.GetProperty("components").GetProperty("schemas");
        foreach (var schemaName in new[] { "QuantityRequest", "QuantityResponse" })
        {
            var schema = schemas.GetProperty(schemaName);
            var branches = schema.GetProperty("oneOf").EnumerateArray().ToArray();
            Assert.Equal(2, branches.Length);
            var measured = Assert.Single(branches, branch => branch.GetProperty("title").GetString() == "Measured quantity");
            var availability = Assert.Single(branches, branch => branch.GetProperty("title").GetString() == "Availability quantity");
            Assert.Equal(["measuredValue", "unit"], measured.GetProperty("required").EnumerateArray().Select(value => value.GetString()!).OrderBy(value => value, StringComparer.Ordinal).ToArray());
            Assert.Equal(["null"], SchemaTypes(measured.GetProperty("properties").GetProperty("availabilityState")));
            Assert.Equal(["availabilityState"], availability.GetProperty("required").EnumerateArray().Select(value => value.GetString()!).ToArray());
            Assert.Equal(["null"], SchemaTypes(availability.GetProperty("properties").GetProperty("measuredValue")));
            Assert.Equal(["null"], SchemaTypes(availability.GetProperty("properties").GetProperty("unit")));
        }

        var create = paths.GetProperty("/api/v1/inventory/lots").GetProperty("post");
        var createExamples = create.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("examples");
        Assert.Equal(JsonValueKind.Null, createExamples.GetProperty("measuredLot").GetProperty("value").GetProperty("quantity").GetProperty("availabilityState").ValueKind);
        Assert.Equal(JsonValueKind.Null, createExamples.GetProperty("availabilityLot").GetProperty("value").GetProperty("quantity").GetProperty("measuredValue").ValueKind);
        Assert.True(create.GetProperty("responses").GetProperty("201").GetProperty("content").GetProperty("application/json").GetProperty("examples").TryGetProperty("completedCreateReplay", out _));
        var adjustment = paths.GetProperty("/api/v1/inventory/lots/{lotId}/adjustments").GetProperty("post");
        Assert.True(adjustment.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("examples").TryGetProperty("completedAdjustmentReplay", out _));
        Assert.True(adjustment.GetProperty("responses").GetProperty("409").GetProperty("content").GetProperty("application/problem+json").GetProperty("examples").TryGetProperty("reusedIdempotencyKey", out _));
        Assert.True(adjustment.GetProperty("responses").GetProperty("412").GetProperty("content").GetProperty("application/problem+json").GetProperty("examples").TryGetProperty("staleEtag", out _));
        Assert.True(adjustment.GetProperty("responses").GetProperty("428").GetProperty("content").GetProperty("application/problem+json").GetProperty("examples").TryGetProperty("missingPrecondition", out _));
        Assert.True(paths.GetProperty("/api/v1/inventory/lots").GetProperty("get").GetProperty("responses").GetProperty("400").GetProperty("content").GetProperty("application/problem+json").GetProperty("examples").TryGetProperty("invalidCursor", out _));
        Assert.True(create.GetProperty("responses").GetProperty("401").GetProperty("content").GetProperty("application/problem+json").GetProperty("examples").TryGetProperty("authenticationFailure", out _));
    }

    private static string[] SchemaTypes(JsonElement schema)
    {
        var type = schema.GetProperty("type");
        return type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(value => value.GetString()!).OrderBy(value => value, StringComparer.Ordinal).ToArray()
            : [type.GetString()!];
    }

    private static string[] EnumValues(JsonElement schema)
    {
        if (schema.TryGetProperty("enum", out var direct))
        {
            return direct.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).ToArray();
        }

        return schema.GetProperty("anyOf").EnumerateArray()
            .Where(branch => branch.TryGetProperty("enum", out _))
            .SelectMany(branch => branch.GetProperty("enum").EnumerateArray())
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()!)
            .ToArray();
    }

    private static WebApplicationFactory<Program> CreateProductionFactory(string clientSecret, string keyRingPath) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("COCINARIS_DB_CONNECTION", "Host=database;Database=cocinaris;Username=cocinaris;Password=valid-secret-value");
            builder.UseSetting("COCINARIS_OIDC_AUTHORITY", "https://identity.example/realms/cocinaris");
            builder.UseSetting("COCINARIS_OIDC_CLIENT_ID", "cocinaris-backend");
            builder.UseSetting("COCINARIS_OIDC_CLIENT_SECRET", clientSecret);
            builder.UseSetting("COCINARIS_SESSION_KEYRING_PATH", keyRingPath);
        });

    private sealed class CapturingActivityExporter(List<Activity> exported) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                exported.Add(activity);
            }

            return ExportResult.Success;
        }
    }

    private sealed class CapturingMetricExporter : BaseExporter<Metric>
    {
        public List<(string Name, IReadOnlyDictionary<string, string> Tags)> Measurements { get; } = [];

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                foreach (ref readonly var point in metric.GetMetricPoints())
                {
                    var tags = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var tag in point.Tags)
                    {
                        tags[tag.Key] = tag.Value?.ToString() ?? string.Empty;
                    }

                    Measurements.Add((metric.Name, tags));
                }
            }

            return ExportResult.Success;
        }
    }
}
