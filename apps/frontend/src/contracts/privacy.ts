/** What the privacy page shows besides fixed copy (PLAN-0049). */
export interface PrivacyCenter {
  /** Where to write about a right the page cannot serve; null hides the line. */
  contactEmail: string | null;
}

/** A downloaded export: the ZIP and the name the server gave it. */
export interface PrivacyExportFile {
  blob: Blob;
  fileName: string;
}

/**
 * The privacy center's server side (PLAN-0049). The export is limited to three per hour per person;
 * past that it rejects with a `RecipeApiError` whose status is 429.
 */
export interface PrivacyRepository {
  getCenter(signal?: AbortSignal): Promise<PrivacyCenter>;
  /** Downloads every record the person has, as a ZIP with JSON and a readable page in `language`. */
  downloadExport(
    language: string,
    signal?: AbortSignal,
  ): Promise<PrivacyExportFile>;
}
