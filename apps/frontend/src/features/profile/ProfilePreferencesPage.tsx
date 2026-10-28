import { useCallback, useState } from "react";
import { Link } from "react-router-dom";
import { useProfileWorkspace } from "./ProfileProvider";
import { createCustomStableCode, isCustomStableCode } from "./customCodes";
import { PREFERENCE_ENTRY_CODES } from "./catalog/profileCatalogCodes";
import { resolveLabel } from "./catalog/profileCatalog";
import { useRegisterUnsavedChanges } from "./UnsavedChangesCoordinator";
import { describeProfileMutationError } from "./describeProfileMutationError";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import {
  type PreferenceCategory,
  type PreferenceEntry,
} from "@/contracts/profile";
import { Button, buttonVariants } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";

/** Backend `PrivateNote` bound (`apps/backend/.../Domain/ProfileValues.cs`). */
const NOTE_MAX_LENGTH = 500;

const CATEGORIES: PreferenceCategory[] = [
  "Preference",
  "Dislike",
  "DietaryPattern",
  "Intolerance",
  "Allergy",
  "ReligiousRestriction",
  "EthicalRestriction",
  "MedicalRestriction",
];

/** Allergy and medical restriction entries require heightened, explicit confirmation. */
const SENSITIVE_CATEGORIES: ReadonlySet<PreferenceCategory> = new Set([
  "Allergy",
  "MedicalRestriction",
]);

interface PendingAdd {
  kind: "catalog" | "custom";
  category: PreferenceCategory;
  stableCode: string;
  note: string | null;
}

/**
 * Category-tabbed preference and restriction editor. Only `confirmed` entries are
 * ever returned by the backend, so every listed row is already active.
 *
 * Custom entries (minted with an opaque `custom_*` stable code, see
 * `./customCodes`) have exactly one piece of user text: the note. That single note
 * *is* the entry's display label — there is deliberately no separate "label" input,
 * since a custom entry has no catalog label to annotate. Catalog entries keep an
 * independent, optional note alongside their fixed catalog label.
 *
 * Allergy and medical restriction additions require an explicit, non-native
 * confirmation dialog (Radix `AlertDialog`: focus trap, Escape-to-cancel, and focus
 * restore all come from Radix) that carries a "not medical advice" disclaimer
 * before the mutation is sent. Typed input for a pending add is preserved verbatim
 * if the confirmation is cancelled or the mutation fails, so the user never has to
 * retype it.
 */
export function ProfilePreferencesPage() {
  const {
    status,
    workspace,
    canMutate,
    isMutating,
    lastMutationError,
    clearMutationError,
    mutatePreferences,
    reload,
  } = useProfileWorkspace();
  const { t, locale } = useProductionI18n();

  const [category, setCategory] = useState<PreferenceCategory>("Preference");
  const [catalogCode, setCatalogCode] = useState("");
  const [catalogNote, setCatalogNote] = useState("");
  const [customLabel, setCustomLabel] = useState("");
  const [noteDrafts, setNoteDrafts] = useState<Record<string, string>>({});
  const [pendingAdd, setPendingAdd] = useState<PendingAdd | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const hasNoteDraftChanges = workspace
    ? workspace.preferences.entries.some((entry) => {
        const draft = noteDrafts[entry.entryId];
        return draft !== undefined && draft !== (entry.note ?? "");
      })
    : false;
  const pageDirty =
    hasNoteDraftChanges ||
    catalogCode.trim() !== "" ||
    catalogNote.trim() !== "" ||
    customLabel.trim() !== "";
  const discardDraft = useCallback(() => {
    setCatalogCode("");
    setCatalogNote("");
    setCustomLabel("");
    setNoteDrafts({});
    setPendingAdd(null);
    setActionError(null);
  }, []);
  useRegisterUnsavedChanges(pageDirty, discardDraft);

  if (status === "session") {
    return (
      <div role="alert" data-testid="profile-preferences-session">
        <p>{t("profile.error.session")}</p>
      </div>
    );
  }

  if ((status === "loading" || status === "idle") && !workspace) {
    return (
      <p role="status" data-testid="profile-preferences-loading">
        {t("profile.loading")}
      </p>
    );
  }

  if (!workspace) {
    return (
      <div role="alert" data-testid="profile-preferences-error">
        <p>{t("profile.error.load")}</p>
        <Button type="button" onClick={() => void reload()}>
          {t("profile.actions.retry")}
        </Button>
      </div>
    );
  }

  const blocked = status === "version_conflict";
  const entries = workspace.preferences.entries
    .filter((entry) => entry.category === category)
    .sort((a, b) => a.sortOrder - b.sortOrder);
  const existingCodes = new Set(entries.map((entry) => entry.stableCode));
  const catalogOptions = PREFERENCE_ENTRY_CODES[category].filter(
    (code) => !existingCodes.has(code),
  );

  async function submitAdd(
    kind: "catalog" | "custom",
    stableCode: string,
    note: string,
  ) {
    setActionError(null);
    clearMutationError();
    try {
      await mutatePreferences([
        {
          action: "add",
          category,
          stableCode,
          note: note.trim() || null,
        },
      ]);
      // Only clear on success: a failed or cancelled add must never discard what
      // the user typed (see class doc above).
      if (kind === "catalog") {
        setCatalogCode("");
        setCatalogNote("");
      } else {
        setCustomLabel("");
      }
    } catch (err) {
      setActionError(describeProfileMutationError(err, t));
    }
  }

  function requestAdd(
    kind: "catalog" | "custom",
    stableCode: string,
    note: string,
  ) {
    if (SENSITIVE_CATEGORIES.has(category)) {
      setPendingAdd({ kind, category, stableCode, note: note.trim() || null });
      return;
    }
    void submitAdd(kind, stableCode, note);
  }

  async function confirmPendingAdd() {
    if (!pendingAdd) return;
    const { kind, stableCode, note } = pendingAdd;
    setPendingAdd(null);
    await submitAdd(kind, stableCode, note ?? "");
  }

  async function removeEntry(entry: PreferenceEntry) {
    setActionError(null);
    clearMutationError();
    try {
      await mutatePreferences([
        {
          action: "remove",
          category: entry.category,
          stableCode: entry.stableCode,
        },
      ]);
    } catch (err) {
      setActionError(describeProfileMutationError(err, t));
    }
  }

  async function updateNote(entry: PreferenceEntry) {
    const nextNote = noteDrafts[entry.entryId] ?? entry.note ?? "";
    setActionError(null);
    clearMutationError();
    try {
      await mutatePreferences([
        {
          action: "update",
          category: entry.category,
          stableCode: entry.stableCode,
          note: nextNote.trim() || null,
        },
      ]);
    } catch (err) {
      setActionError(describeProfileMutationError(err, t));
    }
  }

  return (
    <div
      data-testid="profile-preferences"
      className="mx-auto max-w-2xl space-y-6"
    >
      <div className="flex items-center justify-between gap-2">
        <div>
          <h1 className="font-display text-3xl">
            {t("profile.preferences.title")}
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            {t("profile.preferences.subtitle")}
          </p>
        </div>
        <Link
          to="/app/perfil"
          data-testid="profile-preferences-back"
          className={buttonVariants({ variant: "secondary" })}
        >
          {t("profile.actions.back")}
        </Link>
      </div>

      {blocked && (
        <div role="alert" data-testid="profile-preferences-version-conflict">
          <p>{t("profile.error.versionConflict")}</p>
          <Button type="button" onClick={() => void reload()}>
            {t("profile.actions.reload")}
          </Button>
        </div>
      )}

      {(actionError || lastMutationError) && (
        <p role="alert" data-testid="profile-preferences-error-message">
          {actionError ??
            (lastMutationError
              ? describeProfileMutationError(lastMutationError, t)
              : "")}
        </p>
      )}

      <Tabs
        value={category}
        onValueChange={(value) => {
          setCategory(value as PreferenceCategory);
          setCatalogCode("");
          setCatalogNote("");
          setCustomLabel("");
          setPendingAdd(null);
        }}
      >
        <TabsList className="flex h-auto flex-wrap gap-1">
          {CATEGORIES.map((cat) => (
            <TabsTrigger
              key={cat}
              value={cat}
              data-testid={`profile-preferences-tab-${cat}`}
            >
              {t(`profile.preferences.category.${cat}`)}
            </TabsTrigger>
          ))}
        </TabsList>

        <AlertDialog
          open={pendingAdd !== null}
          onOpenChange={(open) => {
            if (!open) setPendingAdd(null);
          }}
        >
          <AlertDialogContent data-testid="profile-preferences-sensitive-confirm">
            <AlertDialogHeader>
              <AlertDialogTitle>
                {t("profile.preferences.sensitive.title")}
              </AlertDialogTitle>
              <AlertDialogDescription>
                {t("profile.preferences.sensitive.detail")}
              </AlertDialogDescription>
            </AlertDialogHeader>
            <p className="text-sm font-medium">
              {t("profile.preferences.sensitive.noMedicalAdvice")}
            </p>
            <AlertDialogFooter>
              <AlertDialogCancel
                data-testid="profile-preferences-sensitive-cancel"
                disabled={isMutating}
                onClick={() => setPendingAdd(null)}
              >
                {t("profile.preferences.sensitive.cancel")}
              </AlertDialogCancel>
              <AlertDialogAction
                data-testid="profile-preferences-sensitive-confirm-add"
                disabled={!canMutate}
                onClick={(event) => {
                  event.preventDefault();
                  void confirmPendingAdd();
                }}
              >
                {isMutating
                  ? t("profile.preferences.sensitive.submitting")
                  : t("profile.preferences.sensitive.confirmAdd")}
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>

        {/*
        The tab triggers select the category shown here; this panel gives them the
        tabpanel their aria-controls must reference.
      */}
        <TabsContent value={category} className="mt-0 space-y-6">
          <ul className="space-y-2" data-testid="profile-preferences-entries">
            {entries.length === 0 && (
              <li className="text-sm text-muted-foreground">
                {t("profile.preferences.empty")}
              </li>
            )}
            {entries.map((entry) => {
              const isCustom = isCustomStableCode(entry.stableCode);
              const primaryLabel =
                isCustom && entry.note
                  ? entry.note
                  : resolveLabel(locale, category, entry.stableCode);
              return (
                <li
                  key={entry.entryId}
                  data-testid={`profile-preferences-entry-${entry.entryId}`}
                  className="space-y-2 rounded-lg border border-border p-3"
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="font-medium">{primaryLabel}</span>
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      disabled={!canMutate}
                      data-testid={`profile-preferences-remove-${entry.entryId}`}
                      onClick={() => void removeEntry(entry)}
                    >
                      {t("profile.preferences.removeEntry")}
                    </Button>
                  </div>
                  {isCustom && !entry.note && (
                    <p className="text-xs text-muted-foreground">
                      {t("profile.preferences.unknownCode")}
                    </p>
                  )}
                  {isCustom && (
                    <p className="text-xs text-muted-foreground">
                      {t("profile.preferences.customEntryHint")}
                    </p>
                  )}
                  <div className="flex flex-wrap items-center gap-2">
                    <div className="space-y-1">
                      <label
                        htmlFor={`profile-preferences-note-${entry.entryId}`}
                        className="block text-xs"
                      >
                        {t(
                          isCustom
                            ? "profile.preferences.entryCustomName"
                            : "profile.preferences.entryNote",
                          { name: primaryLabel },
                        )}
                      </label>
                      <Input
                        id={`profile-preferences-note-${entry.entryId}`}
                        data-testid={`profile-preferences-note-${entry.entryId}`}
                        className="max-w-xs"
                        maxLength={NOTE_MAX_LENGTH}
                        placeholder={
                          isCustom
                            ? t("profile.preferences.customPlaceholder")
                            : t("profile.preferences.notePlaceholder")
                        }
                        value={noteDrafts[entry.entryId] ?? entry.note ?? ""}
                        disabled={!canMutate}
                        onChange={(event) =>
                          setNoteDrafts((prev) => ({
                            ...prev,
                            [entry.entryId]: event.target.value,
                          }))
                        }
                      />
                    </div>
                    <Button
                      type="button"
                      variant="secondary"
                      size="sm"
                      disabled={!canMutate}
                      data-testid={`profile-preferences-update-note-${entry.entryId}`}
                      onClick={() => void updateNote(entry)}
                    >
                      {isCustom
                        ? t("profile.preferences.renameCustom")
                        : t("profile.preferences.updateNote")}
                    </Button>
                  </div>
                </li>
              );
            })}
          </ul>

          <div
            className="space-y-3 rounded-xl border border-border p-4"
            data-testid="profile-preferences-add-catalog"
          >
            <h2 className="font-medium">
              {t("profile.preferences.addFromCatalog")}
            </h2>
            <div className="flex flex-wrap items-center gap-2">
              <div className="space-y-1">
                <label
                  htmlFor="profile-preferences-catalog-select"
                  className="block text-xs"
                >
                  {t("profile.preferences.catalogSelect")}
                </label>
                <select
                  id="profile-preferences-catalog-select"
                  data-testid="profile-preferences-catalog-select"
                  className="flex h-9 rounded-md border border-input bg-transparent px-3 text-sm"
                  value={catalogCode}
                  disabled={!canMutate}
                  onChange={(event) => setCatalogCode(event.target.value)}
                >
                  <option value="">{t("profile.actions.add")}</option>
                  {catalogOptions.map((code) => (
                    <option key={code} value={code}>
                      {resolveLabel(locale, category, code)}
                    </option>
                  ))}
                </select>
              </div>
              <div className="space-y-1">
                <label
                  htmlFor="profile-preferences-catalog-note"
                  className="block text-xs"
                >
                  {t("profile.preferences.catalogNote")}
                </label>
                <Input
                  id="profile-preferences-catalog-note"
                  data-testid="profile-preferences-catalog-note"
                  className="max-w-xs"
                  maxLength={NOTE_MAX_LENGTH}
                  placeholder={t("profile.preferences.notePlaceholder")}
                  value={catalogNote}
                  disabled={!canMutate}
                  onChange={(event) => setCatalogNote(event.target.value)}
                />
              </div>
              <Button
                type="button"
                disabled={
                  !canMutate ||
                  !catalogCode ||
                  catalogNote.length > NOTE_MAX_LENGTH
                }
                data-testid="profile-preferences-catalog-submit"
                onClick={() => requestAdd("catalog", catalogCode, catalogNote)}
              >
                {t("profile.actions.add")}
              </Button>
            </div>
          </div>

          <div
            className="space-y-3 rounded-xl border border-border p-4"
            data-testid="profile-preferences-add-custom"
          >
            <h2 className="font-medium">
              {t("profile.preferences.addCustom")}
            </h2>
            <p className="text-xs text-muted-foreground">
              {t("profile.preferences.customEntryHint")}
            </p>
            <div className="flex flex-wrap items-center gap-2">
              <div className="space-y-1">
                <label
                  htmlFor="profile-preferences-custom-label"
                  className="block text-xs"
                >
                  {t("profile.preferences.customLabel")}
                </label>
                <Input
                  id="profile-preferences-custom-label"
                  data-testid="profile-preferences-custom-label"
                  className="max-w-xs"
                  maxLength={NOTE_MAX_LENGTH}
                  placeholder={t("profile.preferences.customPlaceholder")}
                  value={customLabel}
                  disabled={!canMutate}
                  aria-invalid={
                    customLabel.length > NOTE_MAX_LENGTH ? true : undefined
                  }
                  aria-describedby={
                    customLabel.length > NOTE_MAX_LENGTH
                      ? "profile-preferences-custom-label-error"
                      : undefined
                  }
                  onChange={(event) => setCustomLabel(event.target.value)}
                />
              </div>
              <Button
                type="button"
                disabled={
                  !canMutate ||
                  !customLabel.trim() ||
                  customLabel.length > NOTE_MAX_LENGTH
                }
                data-testid="profile-preferences-custom-submit"
                onClick={() => {
                  const code = createCustomStableCode();
                  // The free-text the user typed is this custom entry's only piece of
                  // user text: it becomes both the note and (see the primaryLabel
                  // computation above) the display label. The stable code stays opaque
                  // and never embeds user text.
                  requestAdd("custom", code, customLabel.trim());
                }}
              >
                {t("profile.preferences.customSubmit")}
              </Button>
            </div>
            {customLabel.length > NOTE_MAX_LENGTH && (
              <p
                role="alert"
                id="profile-preferences-custom-label-error"
                data-testid="profile-preferences-custom-label-error"
                className="text-xs text-destructive"
              >
                {t("profile.preferences.error.noteTooLong")}
              </p>
            )}
          </div>
        </TabsContent>
      </Tabs>
      {catalogNote.length > NOTE_MAX_LENGTH && (
        <p
          role="alert"
          data-testid="profile-preferences-catalog-note-error"
          className="text-xs text-destructive"
        >
          {t("profile.preferences.error.noteTooLong")}
        </p>
      )}
    </div>
  );
}
