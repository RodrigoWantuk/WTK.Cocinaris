import {
  formatKitchenAmount,
  householdMeasure,
  householdVolume,
} from "./kitchenMeasures";

describe("householdVolume", () => {
  it("expresses exact milliliters in cups and spoons", () => {
    expect(householdVolume(240)).toEqual({
      parts: [{ unit: "cup", amount: 1 }],
      approximate: false,
    });
    expect(householdVolume(30)).toEqual({
      parts: [{ unit: "tbsp", amount: 2 }],
      approximate: false,
    });
    expect(householdVolume(5)?.parts).toEqual([{ unit: "tsp", amount: 1 }]);
    expect(householdVolume(120)?.parts).toEqual([{ unit: "cup", amount: 0.5 }]);
  });

  it("combines a cup fraction and spoons for awkward amounts", () => {
    // 100 ml: ⅓ cup (80) + 1 ½ tbsp (22.5) = 102.5 ml, within 3%; ½ cup alone is 20% off.
    expect(householdVolume(100)?.parts).toEqual([
      { unit: "cup", amount: 1 / 3 },
      { unit: "tbsp", amount: 1.5 },
    ]);
  });

  it("uses a pinch below an eighth of a teaspoon", () => {
    expect(householdVolume(0.3)?.parts).toEqual([{ unit: "pinch", amount: 1 }]);
  });
});

describe("householdMeasure", () => {
  it("converts grams to cups only for a curated ingredient, as an approximation", () => {
    expect(householdMeasure("Arroz branco", 185, "g")).toEqual({
      parts: [{ unit: "cup", amount: 1 }],
      approximate: true,
    });
    expect(householdMeasure("Farinha de trigo", 60, "g")?.parts).toEqual([
      { unit: "cup", amount: 0.5 },
    ]);
    expect(householdMeasure("Peito de frango", 400, "g")).toBeNull();
  });

  it("matches whole words, so parsley is not salt", () => {
    expect(householdMeasure("Salsinha", 5, "g")).toBeNull();
    expect(householdMeasure("Sal", 6, "g")?.parts).toEqual([
      { unit: "tsp", amount: 1 },
    ]);
  });

  it("converts between pieces and grams through an average piece weight", () => {
    expect(householdMeasure("Cebola", 1, "unit")).toEqual({
      parts: [{ unit: "g", amount: 150 }],
      approximate: true,
    });
    expect(householdMeasure("Ovos", 100, "g")?.parts).toEqual([
      { unit: "piece", amount: 2 },
    ]);
    expect(householdMeasure("Pão", 2, "unit")).toBeNull();
  });

  it("never offers a measure that is not compatible", () => {
    expect(householdMeasure("Leite", 0, "ml")).toBeNull();
    expect(householdMeasure("Leite", 3000, "ml")).toBeNull();
    expect(householdMeasure("Tomate", 2000, "g")).toBeNull();
  });
});

describe("formatKitchenAmount", () => {
  it("writes kitchen fractions", () => {
    expect(formatKitchenAmount(1.5)).toBe("1 ½");
    expect(formatKitchenAmount(1 / 3)).toBe("⅓");
    expect(formatKitchenAmount(2)).toBe("2");
  });
});
