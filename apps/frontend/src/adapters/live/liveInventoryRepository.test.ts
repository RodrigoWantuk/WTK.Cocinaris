import {
  createLiveInventoryRepository,
  mapQuantity,
} from "./liveInventoryRepository";
import { InventoryApiError } from "./inventoryTypes";

function jsonResponse(
  body: unknown,
  status = 200,
  headers: Record<string, string> = {},
) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json", ...headers },
  });
}

describe("mapQuantity", () => {
  it("fail-closes malformed qualitative and mixed quantities", () => {
    expect(() =>
      mapQuantity({
        measuredValue: null,
        unit: null,
        availabilityState: null,
        estimated: false,
      }),
    ).toThrow(/Malformed qualitative/);
    expect(() =>
      mapQuantity({
        measuredValue: 1,
        unit: "Gram",
        // Intentionally invalid mixed projection for fail-closed coverage.
        availabilityState: "Low",
      } as never),
    ).toThrow(/Malformed measured/);
  });
});

describe("createLiveInventoryRepository", () => {
  it("lists lots and maps measured quantities", async () => {
    const repo = createLiveInventoryRepository({
      fetchImpl: (async () =>
        jsonResponse({
          items: [
            {
              lotId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
              productId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
              productName: "Rice",
              quantity: {
                measuredValue: 500,
                unit: "Gram",
                availabilityState: null,
              },
              storageLocation: "Pantry",
              customLocation: null,
              packageState: "Sealed",
              printedExpirationDate: "2026-12-31",
              notes: null,
              version: "v1",
              createdAt: "2026-08-01T00:00:00Z",
              updatedAt: "2026-08-01T00:00:00Z",
            },
          ],
          nextCursor: null,
        })) as typeof fetch,
    });
    const page = await repo.listLots({ search: "Rice" });
    expect(page.items).toHaveLength(1);
    expect(page.items[0].quantity).toEqual({
      kind: "measured",
      value: 500,
      unit: "Gram",
    });
    expect(page.items[0].printedExpirationDate).toBe("2026-12-31");
    // This projection predates PLAN-0037 and has no lifecycle.
    expect(page.items[0].lifecycle.state).toBe("AsAcquired");
  });

  it("surfaces 412 without silent retry and requires If-Match", async () => {
    const calls: Array<{ ifMatch: string | null; csrf: string | null }> = [];
    const repo = createLiveInventoryRepository({
      fetchImpl: (async (input) => {
        const request =
          input instanceof Request ? input : new Request(String(input));
        calls.push({
          ifMatch: request.headers.get("If-Match"),
          csrf: request.headers.get("X-CSRF-TOKEN"),
        });
        return new Response(
          JSON.stringify({
            errorCode: "precondition_failed",
            detail: "stale",
          }),
          {
            status: 412,
            headers: { "content-type": "application/problem+json" },
          },
        );
      }) as typeof fetch,
    });

    await expect(
      repo.updateLot(
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        {
          productName: "Rice",
          storageLocation: "Pantry",
          customLocation: null,
          packageState: "Opened",
          printedExpirationDate: null,
          notes: null,
        },
        { csrfToken: "csrf", etag: '"v1"' },
      ),
    ).rejects.toMatchObject({
      code: "precondition_failed",
      status: 412,
    } satisfies Partial<InventoryApiError>);

    expect(calls).toHaveLength(1);
    expect(calls[0].ifMatch).toBe('"v1"');
    expect(calls[0].csrf).toBe("csrf");
  });

  it("maps 428 missing precondition", async () => {
    const repo = createLiveInventoryRepository({
      fetchImpl: (async () =>
        new Response(JSON.stringify({ errorCode: "precondition_required" }), {
          status: 428,
          headers: { "content-type": "application/problem+json" },
        })) as typeof fetch,
    });
    await expect(
      repo.deleteLot("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", {
        csrfToken: "csrf",
        etag: "",
      }),
    ).rejects.toMatchObject({ code: "precondition_required", status: 428 });
  });

  it("reads ETag from create response headers", async () => {
    const repo = createLiveInventoryRepository({
      fetchImpl: (async () =>
        jsonResponse(
          {
            lotId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            productId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            productName: "Rice",
            quantity: {
              measuredValue: 1,
              unit: "Unit",
              availabilityState: null,
            },
            storageLocation: "Pantry",
            customLocation: null,
            packageState: null,
            printedExpirationDate: null,
            notes: null,
            version: "body-version",
            createdAt: "2026-08-01T00:00:00Z",
            updatedAt: "2026-08-01T00:00:00Z",
          },
          201,
          { ETag: '"header-etag"' },
        )) as typeof fetch,
    });
    const created = await repo.createLot(
      {
        productName: "Rice",
        quantity: { kind: "measured", value: 1, unit: "Unit" },
        storageLocation: "Pantry",
      },
      {
        csrfToken: "csrf",
        idempotencyKey: "11111111-1111-1111-1111-111111111111",
      },
    );
    expect(created.etag).toBe('"header-etag"');
  });
});
