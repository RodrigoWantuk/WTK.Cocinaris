import { useState } from "react";
import { UtensilsCrossed } from "lucide-react";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import type { RecipeThumbnail as RecipeThumbnailValue } from "@/contracts/recipes";

/**
 * A recipe's dish image in 16:9 (PLAN-0030). When the image is ready, it shows the generated
 * photo, marked as illustrative and AI-made by `disclose`. While it is being made, or when there
 * is none, it shows a neutral placeholder, so the layout never shifts and the recipe is never
 * blocked. A broken image falls back to the placeholder.
 *
 * @param thumbnail Image state from the API.
 * @param name Recipe name, used in the image's accessible description.
 * @param disclose Show the "imagem ilustrativa gerada por IA" caption (the detail page).
 * @param size `full` (1024 × 576) for the recipe page, `small` (480 × 270) for cards.
 * @param priority Load at once with high priority: the image is the page's main content above
 *   the fold. Cards in lists load lazily.
 * @param className Size and shape classes for the frame.
 */
export function RecipeThumbnail({
  thumbnail,
  name,
  disclose = false,
  size = "full",
  priority = false,
  className = "",
}: {
  thumbnail: RecipeThumbnailValue;
  name: string;
  disclose?: boolean;
  size?: "full" | "small";
  priority?: boolean;
  className?: string;
}) {
  const { t } = useProductionI18n();
  const [broken, setBroken] = useState(false);
  const source =
    size === "small" ? (thumbnail.smallUrl ?? thumbnail.url) : thumbnail.url;
  const ready = thumbnail.state === "ready" && source && !broken;
  return (
    <figure className={className}>
      <div className="aspect-video w-full overflow-hidden rounded-2xl bg-secondary">
        {ready ? (
          <img
            src={source!}
            alt={t("recipes.thumbnail.alt", { name })}
            loading={priority ? "eager" : "lazy"}
            fetchPriority={priority ? "high" : "auto"}
            decoding="async"
            width={size === "small" ? 480 : 1024}
            height={size === "small" ? 270 : 576}
            onError={() => setBroken(true)}
            className="h-full w-full object-cover"
            data-testid="recipe-thumbnail-image"
          />
        ) : (
          <div
            className="flex h-full w-full flex-col items-center justify-center gap-1 text-muted-foreground"
            data-testid="recipe-thumbnail-placeholder"
          >
            <UtensilsCrossed aria-hidden="true" className="h-6 w-6" />
            {thumbnail.state === "pending" && disclose ? (
              <span className="text-xs">{t("recipes.thumbnail.pending")}</span>
            ) : null}
          </div>
        )}
      </div>
      {ready && disclose ? (
        <figcaption className="mt-1 text-xs text-muted-foreground">
          {t("recipes.thumbnail.disclosure")}
        </figcaption>
      ) : null}
    </figure>
  );
}
