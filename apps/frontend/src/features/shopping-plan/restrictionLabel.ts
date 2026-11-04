import { resolveLabel } from "@/features/profile/catalog/profileCatalog";
import type { ProfileCatalogLocale } from "@/features/profile/catalog/profileCatalog";
import type { PreferenceCategory } from "@/contracts/profile";

const CATEGORIES: PreferenceCategory[] = [
  "Allergy",
  "Intolerance",
  "MedicalRestriction",
  "DietaryPattern",
  "ReligiousRestriction",
  "EthicalRestriction",
  "Dislike",
];

/**
 * Display name of a restriction code when its category is not known: the first category with a
 * curated label, else a readable form of the code (custom codes have no label).
 *
 * @param locale Interface locale.
 * @param code Stable restriction code.
 */
export function restrictionLabel(
  locale: ProfileCatalogLocale,
  code: string,
): string {
  // A kind without this code gives the readable fallback, so a different answer is a curated label.
  const readable = resolveLabel(locale, "equipment", code);
  for (const category of CATEGORIES) {
    const label = resolveLabel(locale, category, code);
    if (label !== readable) return label;
  }
  return readable;
}
