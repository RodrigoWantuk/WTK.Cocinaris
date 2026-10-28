using System.Diagnostics;
using Cocinaris.Api.Services;
using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Profiles.Application;
using Microsoft.AspNetCore.WebUtilities;

namespace Cocinaris.Api.Profiles;

/// <summary>HTTP adapter for profile use cases.</summary>
public sealed class ProfileApplicationService(
    IGetProfileUseCase getProfile,
    IPutProfileUseCase putProfile,
    IPatchProfileUseCase patchProfile,
    IGetPreferencesUseCase getPreferences,
    IPutPreferencesUseCase putPreferences,
    IGetEquipmentUseCase getEquipment,
    IPutEquipmentUseCase putEquipment,
    IGetProfileCompletenessUseCase getCompleteness,
    ICurrentUserAccessor currentUser,
    IProfileHttpTokenService tokens,
    ProfileMetrics metrics)
{
    /// <summary>Maps the profile-read query to a transport result.</summary>
    public async Task<IResult> GetAsync(HttpContext context, CancellationToken cancellationToken) =>
        ToProfileResult(isMutation: false, "get", await getProfile.GetAsync(cancellationToken), context.TraceIdentifier);

    /// <summary>Maps the profile-replace command to a transport result.</summary>
    public async Task<IResult> PutAsync(ProfileMutationRequest request, HttpRequest requestContext, CancellationToken cancellationToken) =>
        ToProfileResult(isMutation: true, "put", await putProfile.PutAsync(new PutProfileCommand(ToInput(request), await ReadPreconditionAsync(requestContext, cancellationToken), requestContext.HttpContext.TraceIdentifier), cancellationToken), requestContext.HttpContext.TraceIdentifier);

    /// <summary>Maps the profile-patch command to a transport result.</summary>
    public async Task<IResult> PatchAsync(ProfileMutationRequest request, HttpRequest requestContext, CancellationToken cancellationToken) =>
        ToProfileResult(isMutation: true, "patch", await patchProfile.PatchAsync(new PatchProfileCommand(ToInput(request), await ReadPreconditionAsync(requestContext, cancellationToken), requestContext.HttpContext.TraceIdentifier), cancellationToken), requestContext.HttpContext.TraceIdentifier);

    /// <summary>Maps the preferences-read query to a transport result.</summary>
    public async Task<IResult> GetPreferencesAsync(HttpContext context, CancellationToken cancellationToken) =>
        ToCollectionResult(isMutation: false, "get_preferences", await getPreferences.GetAsync(cancellationToken), collection => ToPreferencesResponse(collection, tokens.WriteVersion), context);

    /// <summary>Maps the preferences-replace command to a transport result.</summary>
    public async Task<IResult> PutPreferencesAsync(PreferencesRequest request, HttpRequest requestContext, CancellationToken cancellationToken) =>
        ToCollectionResult(isMutation: true, "put_preferences", await putPreferences.PutAsync(new PutPreferencesCommand(request.Entries.Select(item => new PreferenceMutationInput(item.Action, item.Category, item.StableCode, item.Note)).ToList(), await ReadPreconditionAsync(requestContext, cancellationToken), requestContext.HttpContext.TraceIdentifier), cancellationToken), collection => ToPreferencesResponse(collection, tokens.WriteVersion), requestContext.HttpContext);

    /// <summary>Maps the equipment-read query to a transport result.</summary>
    public async Task<IResult> GetEquipmentAsync(HttpContext context, CancellationToken cancellationToken) =>
        ToCollectionResult(isMutation: false, "get_equipment", await getEquipment.GetAsync(cancellationToken), collection => ToEquipmentResponse(collection, tokens.WriteVersion), context);

    /// <summary>Maps the equipment-replace command to a transport result.</summary>
    public async Task<IResult> PutEquipmentAsync(EquipmentRequest request, HttpRequest requestContext, CancellationToken cancellationToken) =>
        ToCollectionResult(isMutation: true, "put_equipment", await putEquipment.PutAsync(new PutEquipmentCommand(request.Entries.Select(item => new EquipmentMutationInput(item.StableCode, item.CustomName, item.Capacity, item.CapacityUnit, item.ConstraintNote)).ToList(), await ReadPreconditionAsync(requestContext, cancellationToken), requestContext.HttpContext.TraceIdentifier), cancellationToken), collection => ToEquipmentResponse(collection, tokens.WriteVersion), requestContext.HttpContext);

    /// <summary>Maps the completeness-read query to a transport result.</summary>
    public async Task<IResult> GetCompletenessAsync(HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await getCompleteness.GetAsync(cancellationToken), item => new ProfileCompletenessResponse(item.PercentComplete, item.CompletedSections, item.TotalSections, item.SectionCounts, item.AdultDeclarationState, item.ProfileExists), context.TraceIdentifier);

    private IResult ToProfileResult(bool isMutation, string operation, ProfileApplicationResult<ProfileView> result, string traceId)
    {
        if (isMutation)
        {
            metrics.RecordMutation(operation, result.Problem?.ErrorCode);
        }
        else
        {
            metrics.RecordRead(operation, result.Problem?.ErrorCode);
        }

        if (result.Problem is not null)
        {
            return Problem(result.Problem.ErrorCode, result.Problem.Detail, StatusFor(result.Problem.ErrorCode), traceId, result.Problem.Errors);
        }

        var view = result.Value!;
        if (!view.ProfileExists)
        {
            return Results.Json(ToResponse(view, version: null));
        }

        var version = tokens.WriteVersion(view.OwnerUserId, view.ConcurrencyToken);
        return new EtagResult<ProfileResponse>(ToResponse(view, version), Quote(version), StatusFor(result.Success));
    }

    private IResult ToCollectionResult<TView, TResponse>(
        bool isMutation,
        string operation,
        ProfileApplicationResult<VersionedCollectionView<TView>> result,
        Func<VersionedCollectionView<TView>, TResponse> map,
        HttpContext context)
    {
        if (isMutation)
        {
            metrics.RecordMutation(operation, result.Problem?.ErrorCode);
        }
        else
        {
            metrics.RecordRead(operation, result.Problem?.ErrorCode);
        }

        if (result.Problem is not null)
        {
            return Problem(result.Problem.ErrorCode, result.Problem.Detail, StatusFor(result.Problem.ErrorCode), context.TraceIdentifier, result.Problem.Errors);
        }

        var collection = result.Value!;
        if (collection.ConcurrencyToken == Guid.Empty)
        {
            return Results.Json(map(collection));
        }

        var version = tokens.WriteVersion(collection.OwnerUserId, collection.ConcurrencyToken);
        return new EtagResult<TResponse>(map(collection), Quote(version), StatusCodes.Status200OK);
    }

    private static PreferencesCollectionResponse ToPreferencesResponse(VersionedCollectionView<PreferenceView> collection, Func<Guid, Guid, string> writeVersion) =>
        collection.ConcurrencyToken == Guid.Empty
            ? new PreferencesCollectionResponse(null, Array.Empty<PreferenceResponse>())
            : new PreferencesCollectionResponse(writeVersion(collection.OwnerUserId, collection.ConcurrencyToken), collection.Items.Select(ToResponse).ToList());

    private static EquipmentCollectionResponse ToEquipmentResponse(VersionedCollectionView<EquipmentView> collection, Func<Guid, Guid, string> writeVersion) =>
        collection.ConcurrencyToken == Guid.Empty
            ? new EquipmentCollectionResponse(null, Array.Empty<EquipmentResponse>())
            : new EquipmentCollectionResponse(writeVersion(collection.OwnerUserId, collection.ConcurrencyToken), collection.Items.Select(ToResponse).ToList());

    private static IResult ToResult<TSource, TResponse>(ProfileApplicationResult<TSource> result, Func<TSource, TResponse> map, string traceId) =>
        result.Problem is not null
            ? Problem(result.Problem.ErrorCode, result.Problem.Detail, StatusFor(result.Problem.ErrorCode), traceId, result.Problem.Errors)
            : Results.Json(map(result.Value!));

    private async Task<ProfileVersionPrecondition> ReadPreconditionAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var raw = request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ProfileVersionPrecondition.Missing;
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        return tokens.TryReadVersion(user.Id, raw.Trim('"'), out var version) ? ProfileVersionPrecondition.Valid(version) : ProfileVersionPrecondition.Invalid;
    }

    private static ProfileMutationInput ToInput(ProfileMutationRequest request) => new(
        ToField(request.DisplayName),
        ToField(request.DefaultAdultCount),
        ToField(request.DefaultChildCount),
        ToField(request.DefaultServingCount),
        ToField(request.Language),
        ToField(request.Region),
        ToField(request.Currency),
        ToField(request.MeasurementSystem),
        ToField(request.TimeZone),
        ToField(request.PlanningCadence),
        ToField(request.ShoppingCadence),
        ToField(request.OverallSkill),
        ToField(request.Confidence),
        ToField(request.PreferredInstructionDetail),
        ToField(request.OrdinaryPrepMinutes),
        ToField(request.ExceptionalPrepMinutes),
        ToField(request.EffortTolerance),
        ToField(request.CleanupTolerance),
        ToField(request.RepeatMealPreference),
        ToField(request.ReheatingPreference),
        ToField(request.LeftoverPreference),
        ToField(request.FreezingPreference),
        request.AdultDeclaration is null ? null : new AdultDeclarationMutationInput(request.AdultDeclaration.AdultDeclared, request.AdultDeclaration.TermsVersion, request.AdultDeclaration.PrivacyVersion),
        request.KnownTechniques,
        request.TechniquesToLearn,
        request.Goals,
        request.AbandonmentReasons);

    private static FieldMutation<T>? ToField<T>(FieldMutationDto<T>? field) => field is null ? null : new FieldMutation<T>(field.Action, field.Value, field.Durability);

    private static ProfileResponse ToResponse(ProfileView view, string? version) => new(
        view.OwnerUserId,
        ToField(view.DisplayName),
        new HouseholdDto(
            ToField(view.Household.DefaultAdultCount),
            ToField(view.Household.DefaultChildCount),
            ToField(view.Household.DefaultServingCount),
            ToField(view.Household.Language),
            ToField(view.Household.Region),
            ToField(view.Household.Currency),
            ToField(view.Household.MeasurementSystem),
            ToField(view.Household.TimeZone),
            ToField(view.Household.PlanningCadence),
            ToField(view.Household.ShoppingCadence)),
        new CookingContextDto(
            ToField(view.CookingContext.OverallSkill),
            ToField(view.CookingContext.Confidence),
            ToField(view.CookingContext.PreferredInstructionDetail),
            ToField(view.CookingContext.OrdinaryPrepMinutes),
            ToField(view.CookingContext.ExceptionalPrepMinutes),
            ToField(view.CookingContext.EffortTolerance),
            ToField(view.CookingContext.CleanupTolerance),
            ToField(view.CookingContext.RepeatMealPreference),
            ToField(view.CookingContext.ReheatingPreference),
            ToField(view.CookingContext.LeftoverPreference),
            ToField(view.CookingContext.FreezingPreference)),
        new AdultDeclarationDto(view.AdultDeclaration.AdultDeclared, view.AdultDeclaration.TermsVersion, view.AdultDeclaration.PrivacyVersion, view.AdultDeclaration.AcceptedAt, view.AdultDeclaration.State),
        view.KnownTechniques,
        view.TechniquesToLearn,
        view.Goals,
        view.AbandonmentReasons,
        view.ProfileExists,
        version,
        view.CreatedAt,
        view.UpdatedAt);

    private static ProfileFieldDto<T> ToField<T>(ProfileFieldView<T> field) => new(field.Value, field.Presence, field.DefaultValue, field.Durability);

    private static PreferenceResponse ToResponse(PreferenceView item) => new(item.EntryId, item.Category, item.StableCode, item.Note, item.Presence, item.SortOrder);

    private static EquipmentResponse ToResponse(EquipmentView item) => new(item.EntryId, item.StableCode, item.CustomName, item.Capacity, item.CapacityUnit, item.ConstraintNote, item.IsActive, item.SortOrder);

    private static int StatusFor(ProfileApplicationSuccess success) => success == ProfileApplicationSuccess.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK;

    private static int StatusFor(string errorCode) => errorCode switch
    {
        "validation_failed" => StatusCodes.Status400BadRequest,
        "precondition_required" => StatusCodes.Status428PreconditionRequired,
        "precondition_failed" => StatusCodes.Status412PreconditionFailed,
        "domain_rule_violated" => StatusCodes.Status422UnprocessableEntity,
        "profile_already_exists" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status409Conflict
    };

    private static string Quote(string version) => $"\"{version}\"";

    private static IResult Problem(string errorCode, string detail, int statusCode, string traceId, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var extensions = new Dictionary<string, object?> { ["errorCode"] = errorCode, ["traceId"] = Activity.Current?.Id ?? traceId };
        if (errors is not null)
        {
            extensions["errors"] = errors;
        }

        return Results.Problem(detail: detail, statusCode: statusCode, extensions: extensions);
    }

    private sealed class EtagResult<T>(T body, string etag, int statusCode) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.Headers.ETag = etag;
            return httpContext.Response.WriteAsJsonAsync(body);
        }
    }
}

/// <summary>Protects and parses profile HTTP ETags at the API transport boundary.</summary>
public interface IProfileHttpTokenService
{
    /// <summary>Formats an opaque HTTP ETag from a trusted owner id and concurrency token.</summary>
    string WriteVersion(Guid ownerUserId, Guid concurrencyToken);

    /// <summary>Parses an opaque owner-bound HTTP ETag and rejects tokens for any other owner.</summary>
    /// <param name="expectedOwnerUserId">Authenticated owner id from the current session; never client-supplied.</param>
    /// <param name="token">Opaque ETag body without surrounding HTTP quotes.</param>
    /// <param name="concurrencyToken">Decoded concurrency token when validation succeeds.</param>
    bool TryReadVersion(Guid expectedOwnerUserId, string token, out Guid concurrencyToken);
}

/// <summary>Stateless profile ETag encoder bound to the authenticated owner user identifier.</summary>
public sealed class ProfileHttpTokenService : IProfileHttpTokenService
{
    /// <inheritdoc />
    public string WriteVersion(Guid ownerUserId, Guid concurrencyToken)
    {
        Span<byte> payload = stackalloc byte[32];
        ownerUserId.TryWriteBytes(payload[..16]);
        concurrencyToken.TryWriteBytes(payload[16..]);
        return WebEncoders.Base64UrlEncode(payload);
    }

    /// <inheritdoc />
    public bool TryReadVersion(Guid expectedOwnerUserId, string token, out Guid concurrencyToken)
    {
        concurrencyToken = Guid.Empty;
        try
        {
            var payload = WebEncoders.Base64UrlDecode(token);
            if (payload.Length != 32)
            {
                return false;
            }

            var ownerUserId = new Guid(payload.AsSpan(0, 16));
            if (ownerUserId != expectedOwnerUserId)
            {
                return false;
            }

            concurrencyToken = new Guid(payload.AsSpan(16, 16));
            return concurrencyToken != Guid.Empty;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
