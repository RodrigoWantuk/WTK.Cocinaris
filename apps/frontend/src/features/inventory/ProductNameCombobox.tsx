import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { Input } from "@/components/ui/input";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import type {
  InventoryNameSuggestion,
  InventoryRepository,
} from "@/adapters/live/inventoryTypes";

/** Fewest characters before asking for suggestions (matches the API). */
const MINIMUM_LENGTH = 2;
/** Pause after the last keystroke before asking, so typing stays fluid. */
const DEBOUNCE_MS = 150;

/** Lower case without accents, one output character per input character for Latin text. */
function fold(character: string): string {
  return character.normalize("NFD").replace(/\p{M}/gu, "").toLowerCase();
}

/**
 * Splits `name` around the part that matches `query`, ignoring case and accents, so "acucar"
 * highlights "Açúcar". Each original character is folded on its own and mapped back, so the
 * highlighted range always lands on the original characters. Returns null when nothing matches.
 */
export function splitMatch(
  name: string,
  query: string,
): [string, string, string] | null {
  const needle = Array.from(query.trim()).map(fold).join("");
  if (!needle) return null;
  const characters = Array.from(name);
  let folded = "";
  const origin: number[] = [];
  characters.forEach((character, index) => {
    for (const piece of fold(character)) {
      folded += piece;
      origin.push(index);
    }
  });
  const start = folded.indexOf(needle);
  if (start < 0) return null;
  const from = origin[start];
  const to = origin[start + needle.length - 1] + 1;
  return [
    characters.slice(0, from).join(""),
    characters.slice(from, to).join(""),
    characters.slice(to).join(""),
  ];
}

/**
 * The pantry form's product name field with suggestions (PLAN-0056 Phase 4, owner report 1):
 * while the person types, names they used before and catalog names appear below, with the
 * matching part highlighted, and can be picked with the mouse, touch, or keyboard.
 *
 * Follows the ARIA 1.2 combobox pattern: the input has `role="combobox"`, `aria-expanded`,
 * `aria-controls`, and `aria-activedescendant`; ArrowDown and ArrowUp move through the options,
 * Enter picks the highlighted one, and Escape closes the list. Requests are debounced and the
 * previous one is cancelled. When the repository has no `suggestNames`, or a request fails, the
 * field behaves as a plain text input.
 *
 * @param value Current text.
 * @param onChange Receives every edit and every picked name.
 * @param repository Inventory repository, for `suggestNames`.
 * @param language Interface locale sent with the request.
 */
export function ProductNameCombobox({
  value,
  onChange,
  repository,
  language,
  disabled,
  invalid,
}: {
  value: string;
  onChange: (next: string) => void;
  repository: InventoryRepository;
  language: string;
  disabled?: boolean;
  invalid?: boolean;
}) {
  const { t } = useProductionI18n();
  const listId = useId();
  const [suggestions, setSuggestions] = useState<InventoryNameSuggestion[]>([]);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  // Picking a name sets the text; that change must not reopen the list it was picked from.
  const picked = useRef<string | null>(null);

  useEffect(() => {
    const text = value.trim();
    if (
      !repository.suggestNames ||
      text.length < MINIMUM_LENGTH ||
      picked.current === value
    ) {
      setSuggestions([]);
      return;
    }
    const controller = new AbortController();
    const handle = window.setTimeout(() => {
      repository.suggestNames!(text, { language, signal: controller.signal })
        .then((items) => {
          if (controller.signal.aborted) return;
          // The exact name already typed adds nothing as a suggestion.
          const useful = items.filter(
            (item) => item.name.trim().toLowerCase() !== text.toLowerCase(),
          );
          setSuggestions(useful);
          setActive(-1);
          setOpen(useful.length > 0);
        })
        .catch(() => undefined);
    }, DEBOUNCE_MS);
    return () => {
      window.clearTimeout(handle);
      controller.abort();
    };
  }, [value, repository, language]);

  const expanded = open && suggestions.length > 0;

  function pick(name: string) {
    picked.current = name;
    onChange(name);
    setOpen(false);
    setSuggestions([]);
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "ArrowDown" && suggestions.length > 0) {
      event.preventDefault();
      setOpen(true);
      setActive((index) => (index + 1) % suggestions.length);
    } else if (event.key === "ArrowUp" && expanded) {
      event.preventDefault();
      setActive((index) => (index <= 0 ? suggestions.length - 1 : index - 1));
    } else if (event.key === "Enter" && expanded && active >= 0) {
      // Only an explicit choice is taken; Enter without one still submits the form.
      event.preventDefault();
      pick(suggestions[active].name);
    } else if (event.key === "Escape" && expanded) {
      event.preventDefault();
      setOpen(false);
    }
  }

  return (
    <div className="relative">
      <Input
        data-testid="inventory-product-name"
        role="combobox"
        aria-autocomplete="list"
        aria-expanded={expanded}
        aria-controls={listId}
        aria-activedescendant={
          expanded && active >= 0 ? `${listId}-${active}` : undefined
        }
        autoComplete="off"
        value={value}
        disabled={disabled}
        required
        aria-invalid={invalid}
        onChange={(event) => {
          picked.current = null;
          onChange(event.target.value);
        }}
        onKeyDown={onKeyDown}
        onBlur={() => setOpen(false)}
        onFocus={() => setOpen(suggestions.length > 0)}
      />
      <ul
        id={listId}
        role="listbox"
        aria-label={t("inventory.nameSuggestions.label")}
        hidden={!expanded}
        data-testid="inventory-name-suggestions"
        className="absolute z-20 mt-1 max-h-72 w-full overflow-auto rounded-xl border border-border bg-popover p-1 shadow-lg"
      >
        {suggestions.map((item, index) => {
          const parts = splitMatch(item.name, value);
          return (
            <li
              key={`${item.source}-${item.name}`}
              id={`${listId}-${index}`}
              role="option"
              aria-selected={index === active}
              data-testid={`inventory-name-suggestion-${index}`}
              className={`flex cursor-pointer items-center justify-between gap-2 rounded-lg px-3 py-2 text-[15px] ${
                index === active ? "bg-secondary" : ""
              }`}
              // mousedown keeps focus in the input, so the blur does not close the list first.
              onMouseDown={(event) => event.preventDefault()}
              onClick={() => pick(item.name)}
            >
              <span>
                {parts ? (
                  <>
                    {parts[0]}
                    <mark className="bg-transparent font-semibold text-foreground">
                      {parts[1]}
                    </mark>
                    {parts[2]}
                  </>
                ) : (
                  item.name
                )}
              </span>
              {item.source === "pantry" ? (
                <span className="text-xs text-muted-foreground">
                  {t("inventory.nameSuggestions.usedBefore")}
                </span>
              ) : null}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
