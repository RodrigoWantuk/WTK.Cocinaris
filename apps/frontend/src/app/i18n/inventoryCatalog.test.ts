import {
  REQUIRED_INVENTORY_I18N_KEYS,
  inventoryCatalogs,
} from "./inventoryCatalog";
import { PRODUCTION_LOCALES } from "./productionCatalog";

describe("inventory i18n completeness", () => {
  it.each([...PRODUCTION_LOCALES])(
    "provides every required inventory key for %s",
    (locale) => {
      const catalog = inventoryCatalogs[locale];
      for (const key of REQUIRED_INVENTORY_I18N_KEYS) {
        expect(catalog[key]).toBeTruthy();
        expect(catalog[key]).not.toEqual(key);
      }
    },
  );
});

/**
 * Locale catalogs spread the English catalog first, so a missing translation silently
 * renders English (it once showed "6 units" in Portuguese). Values may equal English
 * only when listed here because they are language-neutral.
 */
const LANGUAGE_NEUTRAL_KEYS: Record<"pt-BR" | "es", readonly string[]> = {
  "pt-BR": [
    "inventory.unit.Gram",
    "inventory.unit.Milliliter",
    // "Status" and "Freezer" are the usual Brazilian Portuguese words.
    "inventory.filter.status",
    "inventory.location.Freezer",
  ],
  es: ["inventory.unit.Gram", "inventory.unit.Milliliter"],
};

describe("inventory i18n translation coverage", () => {
  it.each(["pt-BR", "es"] as const)(
    "does not fall back to English values in %s",
    (locale) => {
      const english = inventoryCatalogs.en;
      const catalog = inventoryCatalogs[locale];
      const untranslated = REQUIRED_INVENTORY_I18N_KEYS.filter(
        (key) =>
          catalog[key] === english[key] &&
          !LANGUAGE_NEUTRAL_KEYS[locale].includes(key),
      );
      expect(untranslated).toEqual([]);
    },
  );
});

describe("inventory history labels match the backend contract", () => {
  // Values emitted by PostgreSqlInventoryLotReadStore (history `kind`) and the
  // InventoryTransactionType enum (history `type`). A missing key renders the raw
  // enum, which once showed "Transaction · Initial" to users.
  const KINDS = ["Transaction", "MetadataCorrection"];
  const TYPES = [
    "Initial",
    "Consume",
    "Discard",
    "Correct",
    "AvailabilityChanged",
    "Deleted",
    "PreparationInputConsumed",
    "PreparationOutputCreated",
  ];

  it.each([...PRODUCTION_LOCALES])(
    "labels every kind and type in %s",
    (locale) => {
      const catalog: Record<string, string> = inventoryCatalogs[locale];
      for (const kind of KINDS) {
        expect(catalog[`inventory.history.kind.${kind}`]).toBeTruthy();
      }
      for (const type of TYPES) {
        expect(catalog[`inventory.adjustment.${type}`]).toBeTruthy();
      }
    },
  );
});
