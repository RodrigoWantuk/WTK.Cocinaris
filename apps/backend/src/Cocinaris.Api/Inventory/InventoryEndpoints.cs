using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Inventory.Application;
using Microsoft.AspNetCore.Antiforgery;
using Cocinaris.Api.Services;
using Cocinaris.Api.Observability;

namespace Cocinaris.Api.Inventory;

/// <summary>Maps inventory HTTP routes to the inventory application-service boundary.</summary>
public static class InventoryEndpoints
{
    /// <summary>Maps the authenticated inventory routes without embedding persistence or domain orchestration in endpoint handlers.</summary>
    public static RouteGroupBuilder MapInventoryEndpoints(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(ValidateCsrfAsync);
        group.AddEndpointFilter(async (context, next) =>
        {
            await context.HttpContext.RequestServices.GetRequiredService<InventoryCalendar>().ResolveAsync(context.HttpContext.RequestAborted);
            return await next(context);
        });
        // Pantry name suggestions while typing (PLAN-0056 Phase 4).
        group.MapGet("/name-suggestions", async (InventoryNameSuggestionService service, string? q, HttpRequest request, CancellationToken cancellationToken) =>
                Results.Json((await service.SuggestAsync(q, CatalogLanguage(request), cancellationToken)).Select(item => new NameSuggestionResponse(item.Name, item.Source)).ToList()))
            .Produces<IReadOnlyList<NameSuggestionResponse>>();
        // Pantry list read into proposed items for review (PLAN-0046 Phase 1); nothing is saved.
        group.MapPost("/list-parse", (PantryListParseRequest body, IngredientCatalog catalog) =>
                body.Text is { Length: > PantryListParseRequest.MaxTextLength }
                    ? Results.Problem(detail: "The list is too long.", statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["errorCode"] = "validation_failed" })
                    : Results.Json(new PantryListParseResponse(PantryListParser.Parse(body.Text, catalog)
                        .Select(ToLineResponse)
                        .ToList())))
            .RequireRateLimiting("mutation")
            .Produces<PantryListParseResponse>()
            .ProducesProblem(400);
        // AI readings (PLAN-0046 Phases 2 and 3): proposals only, on the person's explicit request.
        group.MapPost("/list-parse/ai", async (PantryListParseRequest body, PantryImportService service, HttpRequest request, CancellationToken cancellationToken) =>
                body.Text is { Length: > PantryListParseRequest.MaxTextLength }
                    ? ImportProblem(new PantryImportResult(null, "validation_failed", "The list is too long."))
                    : ImportResult(await service.ReadListAsync(body.Text, request.HttpContext.TraceIdentifier, ImportLanguage(request), cancellationToken)))
            .RequireRateLimiting("mutation")
            .Produces<PantryListParseResponse>()
            .ProducesProblem(400)
            .ProducesProblem(409)
            .ProducesProblem(422)
            .ProducesProblem(503);
        group.MapGet("/receipt-parse/availability", (PantryImportService service) => Results.Json(new ReceiptAvailabilityResponse(service.ReceiptsAvailable)))
            .Produces<ReceiptAvailabilityResponse>();
        group.MapPost("/receipt-parse", ReadReceiptAsync)
            .RequireRateLimiting("mutation")
            .Accepts<byte[]>("image/jpeg", "image/png", "image/webp")
            .Produces<PantryListParseResponse>()
            .ProducesProblem(400)
            .ProducesProblem(409)
            .ProducesProblem(413)
            .ProducesProblem(422)
            .ProducesProblem(503);
        group.MapGet("/attention", (InventoryApplicationService service, HttpContext context, CancellationToken cancellationToken) => service.AttentionAsync(context, cancellationToken)).Produces<AttentionListResponse>();
        group.MapGet("/lots", (InventoryApplicationService service, int? pageSize, string? status, string? storageLocation, string? search, string? cursor, HttpContext context, CancellationToken cancellationToken) => service.ListAsync(pageSize, status, storageLocation, search, cursor, context, cancellationToken)).Produces<ListLotsResponse>().ProducesProblem(400);
        group.MapPost("/lots", (InventoryApplicationService service, CreateLotRequest request, HttpRequest requestContext, CancellationToken cancellationToken) => service.CreateAsync(request, requestContext, cancellationToken)).RequireRateLimiting("mutation").Produces<LotResponse>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(409).ProducesProblem(422);
        group.MapGet("/lots/{lotId:guid}", (InventoryApplicationService service, Guid lotId, HttpContext context, CancellationToken cancellationToken) => service.GetAsync(lotId, context, cancellationToken)).Produces<LotResponse>().ProducesProblem(404);
        group.MapPatch("/lots/{lotId:guid}", (InventoryApplicationService service, Guid lotId, UpdateLotRequest request, HttpRequest requestContext, CancellationToken cancellationToken) => service.UpdateAsync(lotId, request, requestContext, cancellationToken)).RequireRateLimiting("mutation").Produces<LotResponse>().ProducesProblem(404).ProducesProblem(412).ProducesProblem(422).ProducesProblem(428);
        group.MapPost("/lots/{lotId:guid}/adjustments", (InventoryApplicationService service, Guid lotId, AdjustmentRequest request, HttpRequest requestContext, CancellationToken cancellationToken) => service.AdjustAsync(lotId, request, requestContext, cancellationToken)).RequireRateLimiting("mutation").Produces<LotResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).ProducesProblem(412).ProducesProblem(422).ProducesProblem(428);
        group.MapPost("/lots/{lotId:guid}/transitions", (InventoryApplicationService service, Guid lotId, TransitionRequest request, HttpRequest requestContext, CancellationToken cancellationToken) => service.TransitionAsync(lotId, request, requestContext, cancellationToken)).RequireRateLimiting("mutation").Produces<LotResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).ProducesProblem(412).ProducesProblem(422).ProducesProblem(428);
        group.MapDelete("/lots/{lotId:guid}", (InventoryApplicationService service, Guid lotId, HttpRequest requestContext, CancellationToken cancellationToken) => service.DeleteAsync(lotId, requestContext, cancellationToken)).RequireRateLimiting("mutation").Produces(StatusCodes.Status204NoContent).ProducesProblem(404).ProducesProblem(412).ProducesProblem(428);
        group.MapGet("/lots/{lotId:guid}/history", (InventoryApplicationService service, Guid lotId, HttpContext context, CancellationToken cancellationToken) => service.HistoryAsync(lotId, context, cancellationToken)).Produces<IReadOnlyList<LotHistoryResponse>>().ProducesProblem(404);
        group.MapPost("/preparations", (InventoryApplicationService service, PrepareComponentsRequest request, HttpRequest requestContext, CancellationToken cancellationToken) => service.PrepareAsync(request, requestContext, cancellationToken)).RequireRateLimiting("mutation").Produces<PreparationResponse>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).ProducesProblem(412).ProducesProblem(422).ProducesProblem(428);
        group.MapGet("/preparations/{batchId:guid}", (InventoryApplicationService service, Guid batchId, HttpContext context, CancellationToken cancellationToken) => service.GetPreparationAsync(batchId, context, cancellationToken)).Produces<PreparationResponse>().ProducesProblem(404);
        group.MapGet("/lots/{lotId:guid}/provenance", (InventoryApplicationService service, Guid lotId, HttpContext context, CancellationToken cancellationToken) => service.ProvenanceAsync(lotId, context, cancellationToken)).Produces<LotProvenanceResponse>().ProducesProblem(404);
        return group;
    }

    private static async ValueTask<object?> ValidateCsrfAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (HttpMethods.IsPost(context.HttpContext.Request.Method) || HttpMethods.IsPatch(context.HttpContext.Request.Method) || HttpMethods.IsDelete(context.HttpContext.Request.Method))
        {
            try
            {
                await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                context.HttpContext.RequestServices.GetRequiredService<SecurityMetrics>().RecordFailure("csrf");
                return ApiProblem.Create(context.HttpContext, StatusCodes.Status400BadRequest, "validation_failed", "The CSRF token is missing or invalid.");
            }
        }

        return await next(context);
    }

    /// <summary>Catalog language of the request's first supported <c>Accept-Language</c> tag, or null.</summary>
    private static string? CatalogLanguage(HttpRequest request) =>
        request.GetTypedHeaders().AcceptLanguage
            .OrderByDescending(item => item.Quality ?? 1)
            .Select(item => item.Value.Value?.ToLowerInvariant() switch { "pt-br" or "pt" => "pt", "en" or "en-us" or "en-gb" => "en", "es" or "es-es" => "es", _ => null })
            .FirstOrDefault(code => code is not null);

    private static PantryListLineResponse ToLineResponse(PantryListLine line) =>
        new(line.LineNumber, line.Original, line.Name, line.Quantity, line.Unit, line.StorageLocation, line.Recognized, line.Uncertain);

    /// <summary>
    /// Reads the raw request body into memory, within the limit, and hands it to the service. The
    /// photo is deliberately not a form upload: ASP.NET buffers large form files to a temporary disk
    /// file, and a receipt must never touch the disk.
    /// </summary>
    private static async Task<IResult> ReadReceiptAsync(PantryImportService service, HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > PantryImportService.MaxReceiptBytes)
        {
            return Results.Problem(detail: "The photo is too large.", statusCode: StatusCodes.Status413PayloadTooLarge, extensions: new Dictionary<string, object?> { ["errorCode"] = "validation_failed" });
        }

        var buffer = new byte[(int)(request.ContentLength ?? 1024 * 1024)];
        var read = 0;
        while (true)
        {
            if (read == buffer.Length)
            {
                if (buffer.Length >= PantryImportService.MaxReceiptBytes)
                {
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);
                    return Results.Problem(detail: "The photo is too large.", statusCode: StatusCodes.Status413PayloadTooLarge, extensions: new Dictionary<string, object?> { ["errorCode"] = "validation_failed" });
                }

                var larger = new byte[Math.Min(buffer.Length * 2, PantryImportService.MaxReceiptBytes)];
                buffer.CopyTo(larger, 0);
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);
                buffer = larger;
            }

            var count = await request.Body.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        // The service wipes what it receives; the unused tail of the buffer holds nothing.
        var image = read == buffer.Length ? buffer : buffer[..read];
        if (!ReferenceEquals(image, buffer))
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);
        }

        return ImportResult(await service.ReadReceiptAsync(image, request.HttpContext.TraceIdentifier, ImportLanguage(request), cancellationToken));
    }

    private static IResult ImportResult(PantryImportResult result) =>
        result.Lines is { } lines ? Results.Json(new PantryListParseResponse(lines.Select(ToLineResponse).ToList())) : ImportProblem(result);

    private static IResult ImportProblem(PantryImportResult result) => Results.Problem(
        detail: result.Detail,
        statusCode: result.ErrorCode switch
        {
            "validation_failed" => StatusCodes.Status400BadRequest,
            "ai_output_invalid" => StatusCodes.Status422UnprocessableEntity,
            "ai_operation_conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status503ServiceUnavailable
        },
        extensions: new Dictionary<string, object?> { ["errorCode"] = result.ErrorCode });

    private static string? ImportLanguage(HttpRequest request) =>
        request.GetTypedHeaders().AcceptLanguage
            .OrderByDescending(item => item.Quality ?? 1)
            .Select(item => item.Value.Value?.ToLowerInvariant() switch { "pt-br" or "pt" => "pt-BR", "en" or "en-us" or "en-gb" => "en", "es" or "es-es" => "es", _ => null })
            .FirstOrDefault(tag => tag is not null);
}

/// <summary>A suggested pantry product name (PLAN-0056 Phase 4).</summary>
/// <param name="Name">Name to fill in.</param>
/// <param name="Source"><c>pantry</c> (used before) or <c>catalog</c> (curated ingredient).</param>
public sealed record NameSuggestionResponse(string Name, string Source);

/// <summary>Whether this deployment can read receipt photos (PLAN-0046).</summary>
/// <param name="Available">True when the option should be offered.</param>
public sealed record ReceiptAvailabilityResponse(bool Available);

/// <summary>A pantry list to read (PLAN-0046 Phase 1).</summary>
/// <param name="Text">One item per line, at most <see cref="MaxTextLength"/> characters.</param>
public sealed record PantryListParseRequest(string? Text)
{
    /// <summary>Longest list accepted, in characters.</summary>
    public const int MaxTextLength = 8000;
}

/// <summary>The list read into proposed items, in line order; nothing was saved.</summary>
/// <param name="Lines">Proposed items.</param>
public sealed record PantryListParseResponse(IReadOnlyList<PantryListLineResponse> Lines);

/// <summary>One proposed pantry item from a list line.</summary>
/// <param name="LineNumber">1-based line.</param>
/// <param name="Original">The line as typed.</param>
/// <param name="Name">Product name.</param>
/// <param name="Quantity">Amount in <paramref name="Unit"/>; null when none was given.</param>
/// <param name="Unit"><c>Gram</c>, <c>Milliliter</c>, or <c>Unit</c>; null with no amount.</param>
/// <param name="StorageLocation">Suggested place: <c>Pantry</c>, <c>Refrigerator</c>, or <c>Freezer</c>.</param>
/// <param name="Recognized">Whether the name is a curated catalog ingredient.</param>
/// <param name="Uncertain">Whether an AI reading was unsure of this item, so the person should check it.</param>
public sealed record PantryListLineResponse(int LineNumber, string Original, string Name, decimal? Quantity, string? Unit, string StorageLocation, bool Recognized, bool Uncertain);
