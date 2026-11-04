# Pantry list and receipt reading (PLAN-0046)

Three readers feed one review screen (`/app/despensa/lista`). None of them saves anything: the person checks every item, and "Adicionar" then calls the normal create command once per item.

| Reader | Endpoint | AI | When it runs |
|---|---|---|---|
| Plain list | `POST /api/v1/inventory/list-parse` | None; `PantryListParser` | "Ler lista" |
| AI list | `POST /api/v1/inventory/list-parse/ai` | `inventory.list_parse.v1`, DeepSeek | "Organizar com IA" |
| Receipt photo | `POST /api/v1/inventory/receipt-parse` | `inventory.receipt_parse.v1`, OpenRouter vision | "Foto da nota fiscal" |

`GET /api/v1/inventory/receipt-parse/availability` says whether to offer the photo.

## Contract

Both AI operations answer the closed shape in [`pantry-items.response.v0.3.json`](../../packages/contracts/ai/inventory/schemas/pantry-items.response.v0.3.json); the backend sets the `operation` const. `PantryImportService.Validate` is the deterministic gate:

- quantity above 0 and at most 100,000, always with a unit (`g`, `ml`, `unit`), or both null;
- at most 60 items;
- an item whose name has a run of 8 or more digits, a link, or markup is dropped. A source text with any of them is not shown back.

## Hostile input

- **List text:** cleaned line by line by `UntrustedText`, and sent as one JSON string in `list.text` next to a fixed treatment rule.
- **Photo:** must be a JPEG, PNG, or WebP by its own header, at most 12 MB and 50 megapixels before decoding. It is decoded and re-encoded by `SkiaReceiptImagePreparer`. Text in it is declared data by the system prompt.
- **Output:** bounded as above, rendered as plain values in inputs, and only a proposal.

## Budget and failure

Each call reserves usage through `AiUsageGovernor`, settling on success and releasing on failure. The list gets one repair. The receipt gets none: an image is read once, and a failure is reported and never retried. Without the AI, the plain list and manual entry still work; the review screen keeps the typed text after a failure.

## Configuration

| Setting | Default | Purpose |
|---|---|---|
| `Ai__Receipts__Enabled` | `false` | Allows sending receipt photos to the provider |
| `Ai__Receipts__Model` | `openai/gpt-5-nano` | OpenRouter vision model; add a price under `Ai__Pricing` if it changes |
| `Ai__Providers__OpenRouter__ApiKey` | none | The key already used for images and embeddings |

The default model was chosen by listed price and vision support on 2026-11-04. It has not been evaluated on real receipts.

The OpenAPI document describes the receipt body as a byte string because of how the minimal API declares it. The actual body is the raw image bytes with an image content type, not base64.

## Why there is no object storage

The architecture document allows S3-compatible storage for temporary files. This flow does not need it: keeping the photo in memory for one request meets "parsed once and deleted" more strictly than any store. Permanent photos and recipe images from imports remain with PLAN-0047.

## Not delivered

- Package sizes learned from receipts, which stay unscheduled (see [PLAN-0029](../plans/PLAN-0029-implement-sequential-planning-menu-and-shopping.md)).
- Recording on a lot that it came from a receipt: lots created here look like manual entries.
