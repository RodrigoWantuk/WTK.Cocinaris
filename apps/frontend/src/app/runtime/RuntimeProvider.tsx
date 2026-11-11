import {
  createContext,
  createElement,
  useContext,
  type ReactNode,
} from "react";
import type { FrontendRuntime } from "./types";

const RuntimeContext = createContext<FrontendRuntime | null>(null);

/**
 * Provides the composition-root runtime to the React tree.
 */
export function RuntimeProvider({
  runtime,
  children,
}: {
  runtime: FrontendRuntime;
  children: ReactNode;
}) {
  return createElement(RuntimeContext.Provider, { value: runtime }, children);
}

/**
 * Access the injected frontend runtime. Must be used under RuntimeProvider.
 */
export function useRuntime(): FrontendRuntime {
  const ctx = useContext(RuntimeContext);
  if (!ctx) {
    throw new Error("useRuntime must be used within RuntimeProvider");
  }
  return ctx;
}

/**
 * The injected runtime, or null outside RuntimeProvider; for components that use an optional part of
 * it, such as the offline store, and must also render in isolated tests.
 */
export function useOptionalRuntime(): FrontendRuntime | null {
  return useContext(RuntimeContext);
}
