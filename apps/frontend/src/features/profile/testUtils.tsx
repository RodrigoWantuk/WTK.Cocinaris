import type { ReactNode } from "react";
import { createMemoryRouter, RouterProvider, Outlet } from "react-router-dom";
import { ProfileProvider } from "./ProfileProvider";
import { UnsavedChangesCoordinatorProvider } from "./UnsavedChangesCoordinator";
import {
  createUnavailableAdultDeclarationPolicy,
  type AdultDeclarationPolicy,
} from "./adultDeclarationPolicy";
import { SessionProvider } from "@/app/session/SessionProvider";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import type {
  EquipmentSnapshot,
  PreferenceSnapshot,
  ProfileCompleteness,
  ProfileRepository,
  ProfileSnapshot,
  ProgressiveProfileField,
} from "@/contracts/profile";
import type { SessionAdapter, SessionState } from "@/app/session/types";

function field<T>(
  value: T | null,
  presence: ProgressiveProfileField<T>["presence"] = value == null
    ? "absent"
    : "confirmed",
  defaultValue: T | null = null,
): ProgressiveProfileField<T> {
  return { value, presence, defaultValue, durability: "durable" };
}

/** Absent-profile scaffold: `profileExists: false`, every field absent, no version/etag. */
export function createAbsentProfileSnapshot(): ProfileSnapshot {
  return {
    ownerUserId: "11111111-1111-1111-1111-111111111111",
    displayName: field<string>(null),
    household: {
      defaultAdultCount: field<number>(null),
      defaultChildCount: field<number>(null),
      defaultServingCount: field<number>(null),
      language: field<string>(null),
      region: field<string>(null),
      currency: field<string>(null),
      measurementSystem: field<string>(null),
      timeZone: field<string>(null),
      planningCadence: field<string>(null),
      shoppingCadence: field<string>(null),
    },
    cookingContext: {
      overallSkill: field<string>(null),
      confidence: field<string>(null),
      preferredInstructionDetail: field<string>(null),
      ordinaryPrepMinutes: field<number>(null),
      exceptionalPrepMinutes: field<number>(null),
      effortTolerance: field<string>(null),
      cleanupTolerance: field<string>(null),
      repeatMealPreference: field<string>(null),
      reheatingPreference: field<string>(null),
      leftoverPreference: field<string>(null),
      freezingPreference: field<string>(null),
    },
    adultDeclaration: {
      adultDeclared: null,
      termsVersion: null,
      privacyVersion: null,
      acceptedAt: null,
      state: "NotDeclared",
    },
    knownTechniques: [],
    techniquesToLearn: [],
    goals: [],
    abandonmentReasons: [],
    profileExists: false,
    version: null,
    etag: null,
    createdAt: null,
    updatedAt: null,
  };
}

/** Confirmed profile snapshot with a shared version/etag for workspace consistency tests. */
export function createConfirmedProfileSnapshot(
  overrides: Partial<ProfileSnapshot> = {},
): ProfileSnapshot {
  return {
    ownerUserId: "11111111-1111-1111-1111-111111111111",
    displayName: field("Ada"),
    household: {
      defaultAdultCount: field(2),
      defaultChildCount: field(0, "default", 0),
      defaultServingCount: field(2),
      language: field("en"),
      region: field("US"),
      currency: field("USD"),
      measurementSystem: field("Metric"),
      timeZone: field("America/Sao_Paulo"),
      planningCadence: field("Weekly"),
      shoppingCadence: field("Weekly"),
    },
    cookingContext: {
      overallSkill: field("Comfortable"),
      confidence: field("Moderate"),
      preferredInstructionDetail: field("Standard"),
      ordinaryPrepMinutes: field(30),
      exceptionalPrepMinutes: field(90),
      effortTolerance: field("Medium"),
      cleanupTolerance: field("Medium"),
      repeatMealPreference: field("Neutral"),
      reheatingPreference: field("Comfortable"),
      leftoverPreference: field("Comfortable"),
      freezingPreference: field("Neutral"),
    },
    adultDeclaration: {
      adultDeclared: true,
      termsVersion: "v1",
      privacyVersion: "v1",
      acceptedAt: "2026-08-01T00:00:00Z",
      state: "Declared",
    },
    knownTechniques: ["baking"],
    techniquesToLearn: ["sous_vide_cooking"],
    goals: ["eat_healthier"],
    abandonmentReasons: [],
    profileExists: true,
    version: "v1",
    etag: '"v1"',
    createdAt: "2026-08-01T00:00:00Z",
    updatedAt: "2026-08-01T00:00:00Z",
    ...overrides,
  };
}

export function createEmptyPreferenceSnapshot(
  overrides: Partial<PreferenceSnapshot> = {},
): PreferenceSnapshot {
  return { version: "v1", etag: '"v1"', entries: [], ...overrides };
}

export function createEmptyEquipmentSnapshot(
  overrides: Partial<EquipmentSnapshot> = {},
): EquipmentSnapshot {
  return { version: "v1", etag: '"v1"', entries: [], ...overrides };
}

export function createCompleteness(
  overrides: Partial<ProfileCompleteness> = {},
): ProfileCompleteness {
  return {
    percentComplete: 40,
    completedSections: 2,
    totalSections: 5,
    sectionCounts: {},
    adultDeclarationState: "Declared",
    profileExists: true,
    ...overrides,
  };
}

/** Default fake repository: one confirmed profile, no preferences/equipment. */
export function createMockProfileRepo(
  overrides: Partial<ProfileRepository> = {},
): ProfileRepository {
  return {
    getProfile: vi.fn(async () => createConfirmedProfileSnapshot()),
    patchProfile: vi.fn(async () => createConfirmedProfileSnapshot()),
    replaceProfile: vi.fn(async () => createConfirmedProfileSnapshot()),
    getPreferences: vi.fn(async () => createEmptyPreferenceSnapshot()),
    mutatePreferences: vi.fn(async () => createEmptyPreferenceSnapshot()),
    getEquipment: vi.fn(async () => createEmptyEquipmentSnapshot()),
    replaceEquipment: vi.fn(async () => createEmptyEquipmentSnapshot()),
    getCompleteness: vi.fn(async () => createCompleteness()),
    ...overrides,
  };
}

export function createSessionAdapter(
  state: SessionState = {
    status: "authenticated",
    internalUserId: "11111111-1111-1111-1111-111111111111",
    csrfToken: "csrf-test",
    displayName: "Ada",
    timeZone: "UTC",
    supportedLocales: ["en", "pt-BR", "es"],
  },
): SessionAdapter {
  return {
    getSession: vi.fn(async () => state),
    beginLogin: vi.fn(),
    logout: vi.fn(async () => undefined),
  };
}

export function renderProfileTree({
  repository,
  adultPolicy = createUnavailableAdultDeclarationPolicy(),
  sessionAdapter = createSessionAdapter(),
  initialPath = "/app/perfil",
  children,
}: {
  repository: ProfileRepository;
  adultPolicy?: AdultDeclarationPolicy;
  sessionAdapter?: SessionAdapter;
  initialPath?: string;
  children: ReactNode;
}) {
  const router = createMemoryRouter(
    [
      {
        path: "/app/perfil",
        element: (
          <UnsavedChangesCoordinatorProvider>
            <Outlet />
          </UnsavedChangesCoordinatorProvider>
        ),
        children: [
          { index: true, element: children },
          { path: "dados", element: children },
          { path: "preferencias", element: children },
          { path: "equipamentos", element: children },
        ],
      },
      {
        path: "/app/hoje",
        element: <div data-testid="nav-home-destination">home</div>,
      },
      {
        path: "/app/despensa",
        element: <div data-testid="nav-inventory-destination">inventory</div>,
      },
    ],
    { initialEntries: [initialPath] },
  );

  return (
    <ProductionI18nProvider initialLocale="en">
      <SessionProvider adapter={sessionAdapter}>
        <ProfileProvider repository={repository} adultPolicy={adultPolicy}>
          <RouterProvider router={router} />
        </ProfileProvider>
      </SessionProvider>
    </ProductionI18nProvider>
  );
}
