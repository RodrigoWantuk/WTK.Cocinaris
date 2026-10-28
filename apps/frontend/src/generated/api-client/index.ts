/**
 * Cocinaris generated OpenAPI client boundary.
 *
 * Generated members live in `./generated/schema` and must not be edited by hand.
 * Regenerate with `yarn generate` after OpenAPI snapshot changes.
 */
export type { paths, components, operations } from "./generated/schema";
export { createCocinarisClient, type CocinarisClient } from "./createClient";
export {
  readProblemDetails,
  readEtag,
  type CocinarisProblemDetails,
} from "./problemDetails";
