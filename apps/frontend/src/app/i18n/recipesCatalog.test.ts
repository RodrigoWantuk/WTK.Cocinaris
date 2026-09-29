import { REQUIRED_RECIPES_I18N_KEYS, recipesCatalogs } from "./recipesCatalog";
import { PRODUCTION_LOCALES } from "./productionCatalog";

describe("recipes i18n completeness", () => {
  it.each([...PRODUCTION_LOCALES])(
    "provides every required recipes key for %s",
    (locale) => {
      const catalog = recipesCatalogs[locale];
      for (const key of REQUIRED_RECIPES_I18N_KEYS) {
        expect(catalog[key]).toBeTruthy();
        expect(catalog[key]).not.toEqual(key);
      }
    },
  );
});
