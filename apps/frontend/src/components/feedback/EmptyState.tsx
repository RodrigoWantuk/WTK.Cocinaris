import type { ReactNode } from "react";
import type { LucideIcon } from "lucide-react";

/**
 * An empty state that explains and invites (PLAN-0058): an icon in a large accent circle, a
 * short title, one line on what will appear here, and the first action. Used wherever a list can
 * be empty, so an empty screen never reads as broken.
 *
 * @param icon Lucide icon for the kind of content (decorative).
 * @param title What is empty, in the person's words.
 * @param detail What will appear here and how to start.
 * @param actions Buttons or links for the first step.
 * @param testId Test id of the container.
 */
export function EmptyState({
  icon: Icon,
  title,
  detail,
  actions,
  testId,
}: {
  icon: LucideIcon;
  title: string;
  detail?: string;
  actions?: ReactNode;
  testId?: string;
}) {
  return (
    <div
      data-testid={testId}
      role="status"
      className="surface flex flex-col items-center gap-3 px-6 py-10 text-center"
    >
      <span className="flex h-16 w-16 items-center justify-center rounded-full bg-accent text-accent-foreground">
        <Icon aria-hidden="true" className="h-8 w-8" />
      </span>
      <p className="font-display text-xl">{title}</p>
      {detail ? (
        <p className="max-w-sm text-sm text-muted-foreground">{detail}</p>
      ) : null}
      {actions ? (
        <div className="mt-2 flex flex-wrap justify-center gap-2">
          {actions}
        </div>
      ) : null}
    </div>
  );
}
