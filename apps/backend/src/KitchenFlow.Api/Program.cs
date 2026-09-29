using KitchenFlow.Api.Inventory;
using KitchenFlow.Api.Profiles;
using KitchenFlow.Api.Recipes;
using KitchenFlow.Api.Observability;
using KitchenFlow.Api.Services;
using KitchenFlow.Api;
using KitchenFlow.Infrastructure.Persistence;
using KitchenFlow.Modules.Inventory.Application;
using KitchenFlow.Modules.Profiles.Application;
using KitchenFlow.Modules.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Threading.RateLimiting;
using KitchenFlowDataProtectionOptions = KitchenFlow.Api.Services.DataProtectionOptions;
using KitchenFlowSessionOptions = KitchenFlow.Api.Services.SessionOptions;

var builder = WebApplication.CreateBuilder(args);
IdentityModelEventSource.ShowPII = false;
builder.Logging.AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
var isDevelopment = builder.Environment.IsDevelopment();
var configuredConnectionString = builder.Configuration.GetConnectionString("KitchenFlow");
var databaseOptions = new DatabaseOptions { ConnectionString = string.IsNullOrWhiteSpace(configuredConnectionString) ? builder.Configuration["KITCHENFLOW_DB_CONNECTION"] : configuredConnectionString };
var oidcOptions = new OidcOptions
{
    Authority = builder.Configuration["KITCHENFLOW_OIDC_AUTHORITY"] ?? builder.Configuration["Oidc:Authority"],
    ClientId = builder.Configuration["KITCHENFLOW_OIDC_CLIENT_ID"] ?? builder.Configuration["Oidc:ClientId"],
    ClientSecret = builder.Configuration["KITCHENFLOW_OIDC_CLIENT_SECRET"] ?? builder.Configuration["Oidc:ClientSecret"]
};
var dataProtectionOptions = new KitchenFlowDataProtectionOptions { KeyRingPath = builder.Configuration["KITCHENFLOW_SESSION_KEYRING_PATH"] ?? builder.Configuration["DataProtection:KeyRingPath"] };
var sessionOptions = builder.Configuration.GetSection("Session").Get<KitchenFlowSessionOptions>() ?? new KitchenFlowSessionOptions();
var idempotencyOptions = builder.Configuration.GetSection("Idempotency").Get<IdempotencyOptions>() ?? new IdempotencyOptions();
builder.Services.AddOptions<DatabaseOptions>().Configure(options => options.ConnectionString = databaseOptions.ConnectionString).Validate(options => options.IsValid(isDevelopment), "Database configuration is absent or unsafe for this environment.").ValidateOnStart();
builder.Services.AddOptions<OidcOptions>().Configure(options => { options.Authority = oidcOptions.Authority; options.ClientId = oidcOptions.ClientId; options.ClientSecret = oidcOptions.ClientSecret; }).Validate(options => options.IsValid(isDevelopment), "OIDC configuration is absent or unsafe for this environment.").ValidateOnStart();
builder.Services.AddOptions<KitchenFlowDataProtectionOptions>().Configure(options => options.KeyRingPath = dataProtectionOptions.KeyRingPath).Validate(options => options.IsValid(isDevelopment), "Data Protection configuration is absent or unsafe for this environment.").ValidateOnStart();
builder.Services.AddOptions<KitchenFlowSessionOptions>().Configure(options => { options.CookieName = sessionOptions.CookieName; options.IdleTimeout = sessionOptions.IdleTimeout; }).Validate(options => options.IsValid(), "Session configuration must use a __Host- cookie and a timeout between five minutes and one day.").ValidateOnStart();
builder.Services.AddOptions<IdempotencyOptions>()
    .Configure(options => options.Retention = idempotencyOptions.Retention)
    .Validate(options => options.IsValid(), "Idempotency:Retention must be between 1 and 90 days.")
    .ValidateOnStart();
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(databaseOptions.ConnectionString));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<IOidcSubjectAccessor>(provider => provider.GetRequiredService<CurrentUserService>());
builder.Services.AddScoped<IInternalUserStore, PostgreSqlInternalUserStore>();
builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserResolver>();
builder.Services.AddScoped<IInventoryLotReadStore, PostgreSqlInventoryLotReadStore>();
builder.Services.AddScoped<IInventoryLotWriteStore, PostgreSqlInventoryLotWriteStore>();
builder.Services.AddScoped<IInventoryPreparationStore, PostgreSqlInventoryPreparationStore>();
builder.Services.AddSingleton<IInventoryHttpTokenService, DataProtectionInventoryHttpTokenService>();
builder.Services.AddSingleton<InventoryMetrics>();
var securityMetrics = new SecurityMetrics();
builder.Services.AddSingleton(securityMetrics);
builder.Services.AddScoped<InventoryLotApplicationWorkflow>();
builder.Services.AddScoped<ICreateInventoryLotUseCase, CreateInventoryLotHandler>();
builder.Services.AddScoped<IListInventoryLotsUseCase, ListInventoryLotsHandler>();
builder.Services.AddScoped<IGetInventoryLotUseCase, GetInventoryLotHandler>();
builder.Services.AddScoped<IUpdateInventoryLotUseCase, UpdateInventoryLotHandler>();
builder.Services.AddScoped<IAdjustInventoryLotUseCase, AdjustInventoryLotHandler>();
builder.Services.AddScoped<IDeleteInventoryLotUseCase, DeleteInventoryLotHandler>();
builder.Services.AddScoped<IGetInventoryLotHistoryUseCase, GetInventoryLotHistoryHandler>();
builder.Services.AddScoped<PreparedComponentApplicationWorkflow>();
builder.Services.AddScoped<IPrepareComponentsUseCase, PrepareComponentsHandler>();
builder.Services.AddScoped<IGetPreparationBatchUseCase, GetPreparationBatchHandler>();
builder.Services.AddScoped<IGetInventoryLotProvenanceUseCase, GetInventoryLotProvenanceHandler>();
builder.Services.AddScoped<InventoryApplicationService>();
builder.Services.AddSingleton<InventoryLotLifecycleUseCase>();
builder.Services.AddScoped<IProfileReadStore, PostgreSqlProfileReadStore>();
builder.Services.AddScoped<IProfileWriteStore, PostgreSqlProfileWriteStore>();
builder.Services.AddSingleton<IProfileHttpTokenService, ProfileHttpTokenService>();
builder.Services.AddSingleton<ProfileMetrics>();
builder.Services.AddScoped<ProfileApplicationWorkflow>();
builder.Services.AddScoped<IGetProfileUseCase, GetProfileHandler>();
builder.Services.AddScoped<IPutProfileUseCase, PutProfileHandler>();
builder.Services.AddScoped<IPatchProfileUseCase, PatchProfileHandler>();
builder.Services.AddScoped<IGetPreferencesUseCase, GetPreferencesHandler>();
builder.Services.AddScoped<IPutPreferencesUseCase, PutPreferencesHandler>();
builder.Services.AddScoped<IGetEquipmentUseCase, GetEquipmentHandler>();
builder.Services.AddScoped<IPutEquipmentUseCase, PutEquipmentHandler>();
builder.Services.AddScoped<IGetProfileCompletenessUseCase, GetProfileCompletenessHandler>();
builder.Services.AddScoped<IGetProfileSessionProjectionUseCase, GetProfileSessionProjectionHandler>();
builder.Services.AddScoped<ProfileApplicationService>();
builder.Services.AddKitchenFlowRecipeAi(builder.Configuration, builder.Environment);
builder.Services.AddAntiforgery(options => { options.HeaderName = "X-CSRF-TOKEN"; options.Cookie.Name = "__Host-kitchenflow-antiforgery"; options.Cookie.Path = "/"; options.Cookie.SecurePolicy = CookieSecurePolicy.Always; });
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("KitchenFlow");
if (!string.IsNullOrWhiteSpace(dataProtectionOptions.KeyRingPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionOptions.KeyRingPath));
}

var configurationReadiness = new RuntimeConfigurationReadiness(isDevelopment, databaseOptions, oidcOptions, dataProtectionOptions, sessionOptions, idempotencyOptions);
configurationReadiness.ThrowIfInvalidForNonDevelopment();
builder.Services.AddSingleton(configurationReadiness);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = sessionOptions.CookieName;
    options.Cookie.Path = "/";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = sessionOptions.IdleTimeout;
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        securityMetrics.RecordFailure("authentication");
        return ApiProblem.Create(context.HttpContext, StatusCodes.Status401Unauthorized, "authentication_required", "Authentication is required.").ExecuteAsync(context.HttpContext);
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        securityMetrics.RecordFailure("authorization");
        return ApiProblem.Create(context.HttpContext, StatusCodes.Status403Forbidden, "authorization_denied", "Access is denied.").ExecuteAsync(context.HttpContext);
    };
}).AddOpenIdConnect("oidc", options =>
{
    options.Authority = oidcOptions.Authority;
    options.ClientId = oidcOptions.ClientId;
    options.ClientSecret = oidcOptions.ClientSecret;
    options.ResponseType = "code";
    options.UsePkce = true;
    options.SaveTokens = false;
    options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
    options.RequireHttpsMetadata = !isDevelopment;
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        securityMetrics.RecordFailure("rate_limit");
        await ApiProblem.Create(context.HttpContext, StatusCodes.Status429TooManyRequests, "rate_limit_exceeded", "The request rate limit was exceeded.").ExecuteAsync(context.HttpContext);
    };
    options.AddPolicy("authentication", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("mutation", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirst("sub")?.Value ?? context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddOpenApi(options => options.AddDocumentTransformer(async (document, context, cancellationToken) =>
{
    await InventoryOpenApiTransformer.ApplyAsync(document, context, cancellationToken);
    await ProfileOpenApiTransformer.ApplyAsync(document, context, cancellationToken);
}));
var telemetry = builder.Services.AddOpenTelemetry();
telemetry.WithTracing(tracing =>
{
    tracing.AddAspNetCoreInstrumentation().AddEntityFrameworkCoreInstrumentation().AddProcessor(new SensitiveTelemetryRedactionProcessor());
    if (Uri.TryCreate(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"], UriKind.Absolute, out var endpoint))
    {
        tracing.AddOtlpExporter(options => options.Endpoint = endpoint);
    }
});
telemetry.WithMetrics(metrics =>
{
    metrics.AddAspNetCoreInstrumentation().AddMeter("KitchenFlow.Inventory").AddMeter("KitchenFlow.Profiles").AddMeter("KitchenFlow.Security");
    if (Uri.TryCreate(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"], UriKind.Absolute, out var endpoint))
    {
        metrics.AddOtlpExporter(options => options.Endpoint = endpoint);
    }
});

var app = builder.Build();
app.UseExceptionHandler(error => error.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var statusCode = exception is BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError;
    var errorCode = statusCode switch
    {
        StatusCodes.Status400BadRequest => "malformed_request",
        StatusCodes.Status413PayloadTooLarge => "payload_too_large",
        StatusCodes.Status415UnsupportedMediaType => "unsupported_media_type",
        _ => "unexpected_error"
    };
    var detail = statusCode == StatusCodes.Status500InternalServerError ? "An unexpected error occurred." : "The request could not be processed.";
    await ApiProblem.Create(context, statusCode, errorCode, detail).ExecuteAsync(context);
}));
app.UseStatusCodePages(async statusContext =>
{
    var context = statusContext.HttpContext;
    var (errorCode, detail) = context.Response.StatusCode switch
    {
        StatusCodes.Status400BadRequest => ("malformed_request", "The request could not be processed."),
        StatusCodes.Status401Unauthorized => ("authentication_required", "Authentication is required."),
        StatusCodes.Status403Forbidden => ("authorization_denied", "Access is denied."),
        StatusCodes.Status404NotFound => ("resource_not_found", "The requested resource was not found."),
        StatusCodes.Status413PayloadTooLarge => ("payload_too_large", "The request payload is too large."),
        StatusCodes.Status415UnsupportedMediaType => ("unsupported_media_type", "The request content type is not supported."),
        StatusCodes.Status429TooManyRequests => ("rate_limit_exceeded", "The request rate limit was exceeded."),
        StatusCodes.Status503ServiceUnavailable => ("service_unavailable", "A required backend dependency is unavailable."),
        _ => ("unexpected_error", "The request failed.")
    };
    await ApiProblem.Create(context, context.Response.StatusCode, errorCode, detail).ExecuteAsync(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();
app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
app.MapGet("/health/live", () => Results.Ok()).Produces(StatusCodes.Status200OK).AllowAnonymous();
app.MapGet("/health/ready", async (ApplicationDbContext db, RuntimeConfigurationReadiness readiness, CancellationToken ct) => !readiness.IsReady || !await db.Database.CanConnectAsync(ct) ? Results.StatusCode(503) : Results.Ok()).Produces(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status503ServiceUnavailable).AllowAnonymous();
var api = app.MapGroup("/api/v1");
api.MapPost("/auth/login", (string? returnUrl) => Results.Challenge(new AuthenticationProperties { RedirectUri = ReturnUrlPolicy.Normalize(returnUrl) }, ["oidc"])).AllowAnonymous().RequireRateLimiting("authentication").Produces(StatusCodes.Status302Found);
api.MapPost("/auth/logout", async (HttpContext context, IAntiforgery antiforgery, SecurityMetrics metrics) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { metrics.RecordFailure("csrf"); return ApiProblem.Create(context, StatusCodes.Status400BadRequest, "validation_failed", "The CSRF token is missing or invalid."); }
    return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [CookieAuthenticationDefaults.AuthenticationScheme, "oidc"]);
}).RequireAuthorization().Produces(StatusCodes.Status302Found).ProducesProblem(400).ProducesProblem(401);
api.MapGet("/session", async (HttpContext context, IAntiforgery antiforgery, ICurrentUserAccessor currentUser, IGetProfileSessionProjectionUseCase sessionProjection, CancellationToken cancellationToken) =>
{
    var user = await currentUser.GetCurrentAsync(cancellationToken);
    var tokens = antiforgery.GetAndStoreTokens(context);
    var projection = await sessionProjection.GetAsync(user.Id, cancellationToken);
    return Results.Ok(new SessionResponse(user.Id, tokens.RequestToken!, ["en", "pt-BR", "es"], projection.DisplayName, projection.Language, projection.TimeZone, projection.MeasurementSystem, projection.ProfileExists, projection.PercentComplete, projection.AdultDeclarationState.ToString()));
}).RequireAuthorization().Produces<SessionResponse>().ProducesProblem(401);
api.MapGroup("/inventory").RequireAuthorization().MapInventoryEndpoints();
api.MapGroup("/profile").RequireAuthorization().MapProfileEndpoints();
api.MapGroup("/recipes").RequireAuthorization().MapRecipeEndpoints();
app.Run();

/// <summary>Exposes the application entry point to the integration-test host.</summary>
public partial class Program;
