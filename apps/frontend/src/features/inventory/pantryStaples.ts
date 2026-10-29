import type { StorageLocation } from "@/adapters/live/inventoryTypes";

/** A common item offered as a one-tap start on the add screen, with its usual unit and place. */
export type PantryStaple = {
  name: string;
  unit: "Gram" | "Milliliter" | "Unit";
  storage: StorageLocation;
};

type Locale = "pt-BR" | "en" | "es";

// Chosen by interface language until onboarding records the household's region (PLAN-0054
// Phase 6). Names are user-facing copy and become the product name the owner can edit.
const STAPLES: Record<Locale, PantryStaple[]> = {
  "pt-BR": [
    { name: "Arroz", unit: "Gram", storage: "Pantry" },
    { name: "Feijão", unit: "Gram", storage: "Pantry" },
    { name: "Ovos", unit: "Unit", storage: "Refrigerator" },
    { name: "Leite", unit: "Milliliter", storage: "Refrigerator" },
    { name: "Tomate", unit: "Unit", storage: "Refrigerator" },
    { name: "Cebola", unit: "Unit", storage: "Pantry" },
    { name: "Alho", unit: "Unit", storage: "Pantry" },
    { name: "Frango", unit: "Gram", storage: "Freezer" },
    { name: "Macarrão", unit: "Gram", storage: "Pantry" },
  ],
  en: [
    { name: "Eggs", unit: "Unit", storage: "Refrigerator" },
    { name: "Milk", unit: "Milliliter", storage: "Refrigerator" },
    { name: "Bread", unit: "Unit", storage: "Pantry" },
    { name: "Pasta", unit: "Gram", storage: "Pantry" },
    { name: "Rice", unit: "Gram", storage: "Pantry" },
    { name: "Onions", unit: "Unit", storage: "Pantry" },
    { name: "Tomatoes", unit: "Unit", storage: "Refrigerator" },
    { name: "Chicken", unit: "Gram", storage: "Freezer" },
    { name: "Cheese", unit: "Gram", storage: "Refrigerator" },
  ],
  es: [
    { name: "Huevos", unit: "Unit", storage: "Refrigerator" },
    { name: "Leche", unit: "Milliliter", storage: "Refrigerator" },
    { name: "Arroz", unit: "Gram", storage: "Pantry" },
    { name: "Pan", unit: "Unit", storage: "Pantry" },
    { name: "Pasta", unit: "Gram", storage: "Pantry" },
    { name: "Tomates", unit: "Unit", storage: "Refrigerator" },
    { name: "Cebollas", unit: "Unit", storage: "Pantry" },
    { name: "Ajo", unit: "Unit", storage: "Pantry" },
    { name: "Pollo", unit: "Gram", storage: "Freezer" },
  ],
};

/** Staples for the interface language; English for an unknown locale. */
export function staplesFor(locale: string): PantryStaple[] {
  return STAPLES[(locale in STAPLES ? locale : "en") as Locale];
}
