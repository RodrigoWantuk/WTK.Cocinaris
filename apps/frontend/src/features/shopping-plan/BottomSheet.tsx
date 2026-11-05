import * as DialogPrimitive from "@radix-ui/react-dialog";
import { X } from "lucide-react";
import { useId, type ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * A panel that rises from the bottom of the screen (PLAN-0065), built on Radix Dialog rather than a
 * drag drawer: a drawer's gestures fight the on-screen keyboard on iOS, and this sheet has a text box.
 * Radix traps focus, closes on Escape, and returns focus to whatever opened it. On wider screens the
 * same panel sits in the middle. Height is capped at 90% of the dynamic viewport, so the keyboard and
 * browser bars never push the title off screen, and the body scrolls on its own.
 *
 * @param open Whether the sheet is shown.
 * @param onOpenChange Receives `false` when the person closes it.
 * @param title Visible title, also the dialog's accessible name.
 * @param closeLabel Accessible name of the close button.
 * @param header Content kept above the scrolling body, such as a text box.
 * @param footer Content kept below the scrolling body.
 * @param testId Test id of the panel.
 * @param onCloseAutoFocus Overrides where focus goes on close; call `preventDefault` to take over.
 */
export function BottomSheet({
  open,
  onOpenChange,
  title,
  closeLabel,
  header,
  footer,
  children,
  testId,
  onCloseAutoFocus,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  closeLabel: string;
  header?: ReactNode;
  footer?: ReactNode;
  children: ReactNode;
  testId?: string;
  onCloseAutoFocus?: (event: Event) => void;
}) {
  const titleId = useId();
  return (
    <DialogPrimitive.Root open={open} onOpenChange={onOpenChange}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-black/60 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 motion-reduce:animate-none" />
        <DialogPrimitive.Content
          data-testid={testId}
          aria-labelledby={titleId}
          aria-describedby={undefined}
          onCloseAutoFocus={onCloseAutoFocus}
          className={cn(
            "fixed inset-x-0 bottom-0 z-50 flex max-h-[90dvh] flex-col rounded-t-2xl border border-border bg-popover text-popover-foreground shadow-lg",
            "md:inset-x-auto md:bottom-auto md:left-1/2 md:top-1/2 md:w-full md:max-w-lg md:-translate-x-1/2 md:-translate-y-1/2 md:rounded-2xl",
            "duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:slide-out-to-bottom data-[state=open]:slide-in-from-bottom md:data-[state=closed]:slide-out-to-bottom-0 md:data-[state=open]:slide-in-from-bottom-0",
            "motion-reduce:animate-none",
          )}
        >
          <div className="flex items-start gap-2 px-4 pb-2 pt-4">
            <DialogPrimitive.Title
              id={titleId}
              className="min-w-0 flex-1 font-display text-xl leading-tight"
            >
              {title}
            </DialogPrimitive.Title>
            <DialogPrimitive.Close
              aria-label={closeLabel}
              className="-mr-2 -mt-2 flex h-11 w-11 shrink-0 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-secondary hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            >
              <X aria-hidden="true" className="h-5 w-5" />
            </DialogPrimitive.Close>
          </div>
          {header ? <div className="px-4 pb-3">{header}</div> : null}
          <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-4 pb-4">
            {children}
          </div>
          {footer ? (
            <div className="safe-b border-t border-border px-4 pt-3">
              {footer}
            </div>
          ) : (
            <div className="safe-b" />
          )}
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}
