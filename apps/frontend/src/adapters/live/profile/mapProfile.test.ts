import type { components } from "@cocinaris/api-client";
import {
  mapCompleteness,
  mapEquipmentCollection,
  mapPreferencesCollection,
  mapProfilePatchToRequest,
  mapProfileResponse,
} from "./mapProfile";
import {
  ProfileApiError,
  type ProfileFieldMutation,
} from "@/contracts/profile";

type ProfileResponseDto = components["schemas"]["ProfileResponse"];

function projectedField(value: string | null, durability: string = "durable") {
  return {
    value,
    presence: value == null ? "absent" : "confirmed",
    defaultValue: null,
    durability,
  };
}

function projectedIntField(value: number | null, durability = "durable") {
  return {
    value,
    presence: value == null ? "absent" : "confirmed",
    defaultValue: null,
    durability,
  };
}

/** Minimal, otherwise-valid full profile DTO fixture; overridable per field under test. */
function buildProfileDto(
  overrides: Partial<ProfileResponseDto> = {},
): ProfileResponseDto {
  return {
    ownerUserId: "11111111-1111-1111-1111-111111111111",
    displayName: projectedField("Ada"),
    household: {
      defaultAdultCount: projectedIntField(2),
      defaultChildCount: projectedIntField(0),
      defaultServingCount: projectedIntField(2),
      language: projectedField("en"),
      region: projectedField("US"),
      currency: projectedField("USD"),
      measurementSystem: projectedField("Metric"),
      timeZone: projectedField("America/Sao_Paulo"),
      planningCadence: projectedField("Weekly"),
      shoppingCadence: projectedField("Weekly"),
    },
    cookingContext: {
      overallSkill: projectedField("Comfortable"),
      confidence: projectedField("Moderate"),
      preferredInstructionDetail: projectedField("Standard"),
      ordinaryPrepMinutes: projectedIntField(30),
      exceptionalPrepMinutes: projectedIntField(90),
      effortTolerance: projectedField("Medium"),
      cleanupTolerance: projectedField("Medium"),
      repeatMealPreference: projectedField("Neutral"),
      reheatingPreference: projectedField("Comfortable"),
      leftoverPreference: projectedField("Comfortable"),
      freezingPreference: projectedField("Neutral"),
    },
    adultDeclaration: {
      adultDeclared: true,
      termsVersion: "v1",
      privacyVersion: "v1",
      acceptedAt: "2026-08-01T00:00:00Z",
      state: "Declared",
    },
    knownTechniques: [],
    techniquesToLearn: [],
    goals: [],
    abandonmentReasons: [],
    profileExists: true,
    version: "v1",
    createdAt: "2026-08-01T00:00:00Z",
    updatedAt: "2026-08-01T00:00:00Z",
    ...overrides,
  } as ProfileResponseDto;
}

describe("mapProfile — projection durability (read side)", () => {
  it("maps a durable projected field distinctly from a temporary one", () => {
    const dto = buildProfileDto({
      displayName: projectedField("Ada", "durable"),
    });

    const profile = mapProfileResponse(dto, '"v1"');

    expect(profile.displayName.durability).toBe("durable");
  });

  it("maps a temporary projected field when the backend resolves one from request-scoped context", () => {
    const dto = buildProfileDto({
      household: {
        ...buildProfileDto().household,
        // A progressive default resolved from request-scoped context (for example
        // a locale header) rather than durable profile state.
        language: projectedField("en", "temporary"),
      },
    });

    const profile = mapProfileResponse(dto, '"v1"');

    expect(profile.household.language.durability).toBe("temporary");
    // The rest of the profile is unaffected: this is a per-field wire value, not a
    // workspace-wide state.
    expect(profile.displayName.durability).toBe("durable");
  });

  it("fails closed on a durability value that is neither durable nor temporary", () => {
    const dto = buildProfileDto({
      displayName: projectedField("Ada", "eternal"),
    });

    expect(() => mapProfileResponse(dto, '"v1"')).toThrow(ProfileApiError);
    try {
      mapProfileResponse(dto, '"v1"');
    } catch (err) {
      expect(err).toMatchObject({ code: "malformed" });
    }
  });
});

describe("mapProfile — mutation durability (write side)", () => {
  it("omits the wire durability property when the mutation does not specify one", () => {
    const request = mapProfilePatchToRequest({
      displayName: { action: "confirm", value: "Bea" },
    });

    expect(request.displayName).toMatchObject({
      action: "confirm",
      value: "Bea",
    });
    expect(request.displayName?.durability).toBeUndefined();
  });

  it("emits durable on the wire when the mutation explicitly requests it", () => {
    const request = mapProfilePatchToRequest({
      displayName: { action: "confirm", value: "Bea", durability: "durable" },
    });

    expect(request.displayName).toMatchObject({
      action: "confirm",
      value: "Bea",
      durability: "durable",
    });
  });

  it("fails closed before any network call if untrusted runtime input smuggles a temporary mutation durability past the contract's own durable-only type", () => {
    // The `ProfileFieldMutation` contract type only accepts `"durable"`, so this can
    // only happen via untrusted runtime input (for example a value threaded through
    // from an external caller or deserialized data) bypassing the type system, not
    // through normal typed application code. The mapper must still reject it here,
    // before the request would otherwise be sent to the backend.
    const untrusted = {
      action: "confirm",
      value: "Bea",
      durability: "temporary",
    } as unknown as ProfileFieldMutation<string>;

    expect(() => mapProfilePatchToRequest({ displayName: untrusted })).toThrow(
      ProfileApiError,
    );
    try {
      mapProfilePatchToRequest({ displayName: untrusted });
    } catch (err) {
      expect(err).toMatchObject({ code: "malformed" });
    }
  });

  it("fails closed on any other unrecognized mutation durability value", () => {
    const untrusted = {
      action: "confirm",
      value: "Bea",
      durability: "eternal",
    } as unknown as ProfileFieldMutation<string>;

    expect(() => mapProfilePatchToRequest({ displayName: untrusted })).toThrow(
      ProfileApiError,
    );
  });

  it("applies the same durable-only validation to numeric field mutations", () => {
    const untrusted = {
      action: "confirm",
      value: 5,
      durability: "temporary",
    } as unknown as ProfileFieldMutation<number>;

    expect(() =>
      mapProfilePatchToRequest({ defaultAdultCount: untrusted }),
    ).toThrow(ProfileApiError);
  });
});

describe("mapProfile — required and optional numeric fail-closed mapping", () => {
  it("accepts valid zero and positive required integers", () => {
    expect(
      mapCompleteness({
        percentComplete: 0,
        completedSections: 0,
        totalSections: 5,
        sectionCounts: { household: 0 },
        adultDeclarationState: "NotDeclared",
        profileExists: true,
      }).percentComplete,
    ).toBe(0);
    expect(
      mapPreferencesCollection(
        {
          version: "v1",
          entries: [
            {
              entryId: "22222222-2222-2222-2222-222222222222",
              category: "Preference",
              stableCode: "pref_demo",
              note: null,
              presence: "confirmed",
              sortOrder: "0",
            },
          ],
        },
        '"v1"',
      ).entries[0].sortOrder,
    ).toBe(0);
  });

  it.each([
    ["null", null],
    ["undefined", undefined],
    ["empty string", ""],
    ["whitespace", "  "],
    ["NaN", Number.NaN],
    ["Infinity", Number.POSITIVE_INFINITY],
    ["fractional", 1.5],
    ["non-numeric string", "abc"],
  ])("fails closed when completeness.percentComplete is %s", (_label, raw) => {
    expect(() =>
      mapCompleteness({
        percentComplete: raw as number,
        completedSections: 1,
        totalSections: 5,
        sectionCounts: {},
        adultDeclarationState: "NotDeclared",
        profileExists: true,
      }),
    ).toThrow(ProfileApiError);
  });

  it("fails closed when completeness.percentComplete is out of 0..100", () => {
    expect(() =>
      mapCompleteness({
        percentComplete: 101,
        completedSections: 1,
        totalSections: 5,
        sectionCounts: {},
        adultDeclarationState: "NotDeclared",
        profileExists: true,
      }),
    ).toThrow(ProfileApiError);
  });

  it("fails closed on nullish or blank preference and equipment sortOrder", () => {
    expect(() =>
      mapPreferencesCollection(
        {
          version: "v1",
          entries: [
            {
              entryId: "22222222-2222-2222-2222-222222222222",
              category: "Preference",
              stableCode: "pref_demo",
              note: null,
              presence: "confirmed",
              sortOrder: null as unknown as number,
            },
          ],
        },
        '"v1"',
      ),
    ).toThrow(ProfileApiError);
    expect(() =>
      mapEquipmentCollection(
        {
          version: "v1",
          entries: [
            {
              entryId: "33333333-3333-3333-3333-333333333333",
              stableCode: "oven",
              customName: null,
              capacity: null,
              capacityUnit: null,
              constraintNote: null,
              isActive: true,
              sortOrder: "" as unknown as number,
            },
          ],
        },
        '"v1"',
      ),
    ).toThrow(ProfileApiError);
  });

  it("fails closed on malformed sectionCounts values", () => {
    expect(() =>
      mapCompleteness({
        percentComplete: 20,
        completedSections: 1,
        totalSections: 5,
        sectionCounts: { household: null as unknown as number },
        adultDeclarationState: "NotDeclared",
        profileExists: true,
      }),
    ).toThrow(ProfileApiError);
  });

  it("keeps optional capacity null and rejects a present malformed capacity", () => {
    const ok = mapEquipmentCollection(
      {
        version: "v1",
        entries: [
          {
            entryId: "33333333-3333-3333-3333-333333333333",
            stableCode: "oven",
            customName: null,
            capacity: null,
            capacityUnit: null,
            constraintNote: null,
            isActive: true,
            sortOrder: 0,
          },
        ],
      },
      '"v1"',
    );
    expect(ok.entries[0].capacity).toBeNull();
    expect(() =>
      mapEquipmentCollection(
        {
          version: "v1",
          entries: [
            {
              entryId: "33333333-3333-3333-3333-333333333333",
              stableCode: "oven",
              customName: null,
              capacity: "" as unknown as number,
              capacityUnit: null,
              constraintNote: null,
              isActive: true,
              sortOrder: 0,
            },
          ],
        },
        '"v1"',
      ),
    ).toThrow(ProfileApiError);
  });
});
