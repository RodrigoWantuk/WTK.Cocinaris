import type { RecipeApiErrorCode } from "@/contracts/recipes";

/** Maps recipe API error codes to production i18n keys. */
export function recipeErrorMessageKey(code: RecipeApiErrorCode): string {
  switch (code) {
    case "ai_capability_unavailable":
    case "ai_provider_unavailable":
    case "ai_provider_timeout":
    case "unavailable":
      return "recipes.error.providerUnavailable";
    case "ai_budget_exhausted":
    case "ai_budget_unavailable":
      return "recipes.error.budgetUnavailable";
    case "ai_output_invalid":
    case "domain_rule_violated":
      return "recipes.error.invalidOutput";
    case "cancelled":
      return "recipes.generate.cancelled";
    case "authentication_required":
    case "validation_failed":
      return "recipes.error.session";
    case "not_found":
      return "recipes.detail.notFound";
    default:
      return "recipes.error.unexpected";
  }
}

export function isRetryableRecipeError(code: RecipeApiErrorCode): boolean {
  return (
    code === "ai_capability_unavailable" ||
    code === "ai_provider_unavailable" ||
    code === "ai_provider_timeout" ||
    code === "ai_budget_exhausted" ||
    code === "ai_budget_unavailable" ||
    code === "ai_output_invalid" ||
    code === "domain_rule_violated" ||
    code === "unavailable" ||
    code === "conflict" ||
    code === "ai_operation_conflict" ||
    code === "unexpected"
  );
}
