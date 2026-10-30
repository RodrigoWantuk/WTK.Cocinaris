import { Link } from "react-router-dom";
import { Clock, Star } from "lucide-react";
import type { RecipeThumbnail as RecipeThumbnailValue } from "@/contracts/recipes";
import { RecipeThumbnail } from "./RecipeThumbnail";

/**
 * A recipe as a visual card (PLAN-0058): the dish image (or its initial on the accent tint), the
 * name in up to two lines, and one line of metadata. The whole card is one link; an optional
 * favorite toggle sits on the image as a separate button, so the two are never nested.
 *
 * Used on "Hoje" (horizontal carousel) and in the recipe library (grid). Hover lifts the border
 * and tints the surface; the press scale only applies when motion is allowed.
 *
 * @param recipeId Recipe to open.
 * @param name Recipe name; also the image's accessible description.
 * @param thumbnail Image state; a missing one shows the placeholder.
 * @param meta Short facts joined by " · " (meal, servings, times cooked).
 * @param minutes Total time, shown with a clock icon when known.
 * @param favorite Favorite toggle state and handler; omitted, no toggle is shown.
 * @param testId Test id of the card link.
 * @param className Width or grid placement of the card.
 */
export function RecipeCard({
  recipeId,
  name,
  thumbnail,
  meta,
  minutes,
  favorite,
  testId,
  className = "",
}: {
  recipeId: string;
  name: string;
  thumbnail: RecipeThumbnailValue | undefined;
  meta: string[];
  minutes?: number | null;
  favorite?: {
    active: boolean;
    label: string;
    disabled?: boolean;
    onToggle: () => void;
    testId?: string;
  };
  testId?: string;
  className?: string;
}) {
  return (
    <div className={`group relative ${className}`}>
      <Link
        to={`/app/receitas/${recipeId}`}
        data-testid={testId}
        className="surface block h-full overflow-hidden p-2 transition-[background-color,border-color,transform] duration-150 ease-out hover:border-input/60 hover:bg-secondary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background motion-safe:active:scale-[0.99]"
      >
        <RecipeThumbnail
          thumbnail={thumbnail ?? { state: "none", url: null }}
          name={name}
          size="small"
        />
        <span className="block px-1.5 pb-1.5 pt-2.5">
          <span className="line-clamp-2 text-[15px] font-semibold leading-snug">
            {name}
          </span>
          {meta.length > 0 || minutes ? (
            <span className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-[13px] text-muted-foreground">
              {minutes ? (
                <span className="tabular inline-flex items-center gap-1">
                  <Clock aria-hidden="true" className="h-3.5 w-3.5" />
                  {minutes} min
                </span>
              ) : null}
              {meta.length > 0 ? <span>{meta.join(" · ")}</span> : null}
            </span>
          ) : null}
        </span>
      </Link>
      {favorite ? (
        <button
          type="button"
          data-testid={favorite.testId}
          aria-pressed={favorite.active}
          aria-label={favorite.label}
          disabled={favorite.disabled}
          onClick={favorite.onToggle}
          className="absolute right-4 top-4 flex h-9 w-9 items-center justify-center rounded-full bg-card/90 text-foreground shadow-sm ring-1 ring-border transition-colors duration-150 hover:bg-card focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50"
        >
          <Star
            aria-hidden="true"
            className={`h-4 w-4 ${favorite.active ? "fill-primary text-primary" : ""}`}
          />
        </button>
      ) : null}
    </div>
  );
}
