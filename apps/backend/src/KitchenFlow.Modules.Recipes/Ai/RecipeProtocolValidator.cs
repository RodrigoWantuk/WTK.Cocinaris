using System.Text.Json;

namespace KitchenFlow.Modules.Recipes.Ai;

/// <summary>Outcome of validating one untrusted provider response against protocol 0.3.</summary>
/// <typeparam name="T">The closed response DTO type produced on success.</typeparam>
public sealed record RecipeProtocolValidationResult<T>(bool IsValid, T? Response, IReadOnlyList<string> Errors)
{
    /// <summary>Creates a successful validation result.</summary>
    public static RecipeProtocolValidationResult<T> Success(T response) => new(true, response, []);

    /// <summary>Creates a failed validation result.</summary>
    public static RecipeProtocolValidationResult<T> Failure(IReadOnlyList<string> errors) => new(false, default, errors);
}

/// <summary>
/// Deterministic gate between untrusted AI provider output and any downstream persistence or use.
/// Applies structural (closed JSON Schema 2020-12 parity) validation followed by semantic
/// (request-fidelity and content-policy) validation, exactly as
/// <c>packages/contracts/ai/recipe/lib/validate-core.mjs</c> does for the JavaScript contract suite.
/// </summary>
public static class RecipeProtocolValidator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Validates one raw <c>recipe.suggest_candidates.v1</c> provider response.</summary>
    public static RecipeProtocolValidationResult<SuggestCandidatesResponse> ValidateSuggest(string rawJson, JsonElement? request = null)
    {
        SuggestCandidatesResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<SuggestCandidatesResponse>(rawJson, SerializerOptions);
        }
        catch (JsonException exception)
        {
            return RecipeProtocolValidationResult<SuggestCandidatesResponse>.Failure([$"invalid JSON or missing required field: {exception.Message}"]);
        }

        if (response is null)
        {
            return RecipeProtocolValidationResult<SuggestCandidatesResponse>.Failure(["response is not a JSON object"]);
        }

        var errors = response.Validate();
        if (errors.Count > 0)
        {
            return RecipeProtocolValidationResult<SuggestCandidatesResponse>.Failure(errors);
        }

        var semanticErrors = RecipeSuggestSemantics.Validate(response, request);
        return semanticErrors.Count > 0
            ? RecipeProtocolValidationResult<SuggestCandidatesResponse>.Failure(semanticErrors)
            : RecipeProtocolValidationResult<SuggestCandidatesResponse>.Success(response);
    }

    /// <summary>Validates one raw <c>recipe.expand_selected.v1</c> provider response.</summary>
    public static RecipeProtocolValidationResult<ExpandSelectedResponse> ValidateExpand(string rawJson, JsonElement? request = null)
    {
        ExpandSelectedResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<ExpandSelectedResponse>(rawJson, SerializerOptions);
        }
        catch (JsonException exception)
        {
            return RecipeProtocolValidationResult<ExpandSelectedResponse>.Failure([$"invalid JSON or missing required field: {exception.Message}"]);
        }

        if (response is null)
        {
            return RecipeProtocolValidationResult<ExpandSelectedResponse>.Failure(["response is not a JSON object"]);
        }

        var errors = response.Validate();
        if (errors.Count > 0)
        {
            return RecipeProtocolValidationResult<ExpandSelectedResponse>.Failure(errors);
        }

        var semanticErrors = RecipeExpandSemantics.Validate(response, request);
        return semanticErrors.Count > 0
            ? RecipeProtocolValidationResult<ExpandSelectedResponse>.Failure(semanticErrors)
            : RecipeProtocolValidationResult<ExpandSelectedResponse>.Success(response);
    }
}
