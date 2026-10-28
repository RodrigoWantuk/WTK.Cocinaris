import "@testing-library/jest-dom/vitest";
import { webcrypto } from "crypto";
import { TextDecoder, TextEncoder } from "util";

// react-router@7 expects Web TextEncoder in the jsdom test environment.
Object.assign(globalThis, { TextEncoder, TextDecoder });

// jsdom does not expose Web Crypto; inventory mutations need randomUUID.
if (
  typeof globalThis.crypto === "undefined" ||
  typeof globalThis.crypto.randomUUID !== "function"
) {
  Object.defineProperty(globalThis, "crypto", {
    value: webcrypto,
    configurable: true,
  });
}

// Browsers resolve a relative Request URL against the document; Node's Request, which
// Vitest's jsdom environment exposes, rejects it. The API client builds requests from
// same-origin relative paths, so mirror browser semantics here.
const NodeRequest = globalThis.Request;
class DocumentRelativeRequest extends NodeRequest {
  constructor(input: RequestInfo | URL, init?: RequestInit) {
    super(
      typeof input === "string" && input.startsWith("/")
        ? new URL(input, window.location.href)
        : input,
      init,
    );
  }
}
globalThis.Request = DocumentRelativeRequest as typeof Request;
