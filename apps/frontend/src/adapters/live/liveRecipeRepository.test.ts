import { createLiveRecipeRepository } from "./liveRecipeRepository";
import { RecipeApiError } from "@/contracts/recipes";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

function problemResponse(
  errorCode: string,
  status: number,
  detail = errorCode,
): Response {
  return new Response(JSON.stringify({ errorCode, detail, title: errorCode }), {
    status,
    headers: { "content-type": "application/problem+json" },
  });
}

function asRequest(input: RequestInfo | URL): Request {
  return input instanceof Request ? input : new Request(String(input));
}

const candidate = (id: string) => ({
  candidateId: id,
  candidateStrategy: "prefer_inventory",
  name: `Dish ${id}`,
  targetMealType: "dinner",
  dishFormat: "plate",
  primaryTechnique: "saute",
  primaryIngredientRefs: ["egg"],
  summary: `Summary ${id}`,
  servings: 2,
  activeMinutes: 10,
  passiveMinutes: 5,
  totalMinutes: 15,
  difficulty: "easy",
  requiredEquipmentIds: [],
  requiredCapabilities: [],
});

describe("createLiveRecipeRepository", () => {
  it("lists recipes and maps camelCase projections", async () => {
    const fetchImpl = jest.fn(async () =>
      jsonResponse([
        {
          recipeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          name: "Eggs",
          mealTypes: ["breakfast"],
          servings: 1,
          createdAt: "2026-08-05T12:00:00Z",
        },
      ]),
    );
    const repo = createLiveRecipeRepository({ fetchImpl });
    const items = await repo.listRecipes();
    expect(items).toHaveLength(1);
    expect(items[0].name).toBe("Eggs");
    expect(fetchImpl).toHaveBeenCalled();
    const calls = fetchImpl.mock.calls as unknown as Array<
      [RequestInfo | URL, RequestInit?]
    >;
    const request = asRequest(calls[0][0]);
    expect(request.url).toContain("/api/v1/recipes");
    expect(request.method).toBe("GET");
  });

  it("posts generation sessions with CSRF and Idempotency-Key", async () => {
    const fetchImpl = jest.fn(async () =>
      jsonResponse({
        sessionId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
        status: "CandidatesReady",
        candidates: [candidate("c1"), candidate("c2"), candidate("c3")],
        failureReason: null,
      }),
    );
    const repo = createLiveRecipeRepository({ fetchImpl });
    const session = await repo.requestCandidates({
      csrfToken: "csrf-token",
      idempotencyKey: "11111111-1111-1111-1111-111111111111",
    });
    expect(session.candidates).toHaveLength(3);
    expect(fetchImpl).toHaveBeenCalled();
    const calls = fetchImpl.mock.calls as unknown as Array<
      [RequestInfo | URL, RequestInit?]
    >;
    const request = asRequest(calls[0][0]);
    expect(request.url).toContain("/api/v1/recipes/generation-sessions");
    expect(request.method).toBe("POST");
    expect(request.headers.get("X-CSRF-TOKEN")).toBe("csrf-token");
    expect(request.headers.get("Idempotency-Key")).toBe(
      "11111111-1111-1111-1111-111111111111",
    );
  });

  it("maps ai_output_invalid problem details", async () => {
    const fetchImpl = jest.fn(async () =>
      problemResponse("ai_output_invalid", 422, "Invalid"),
    );
    const repo = createLiveRecipeRepository({ fetchImpl });
    await expect(
      repo.requestCandidates({
        csrfToken: "csrf",
        idempotencyKey: "22222222-2222-2222-2222-222222222222",
      }),
    ).rejects.toMatchObject({
      code: "ai_output_invalid",
      status: 422,
    } satisfies Partial<RecipeApiError>);
  });

  it("maps budget unavailable", async () => {
    const fetchImpl = jest.fn(async () =>
      problemResponse("ai_budget_exhausted", 503),
    );
    const repo = createLiveRecipeRepository({ fetchImpl });
    await expect(
      repo.requestCandidates({
        csrfToken: "csrf",
        idempotencyKey: "33333333-3333-3333-3333-333333333333",
      }),
    ).rejects.toMatchObject({ code: "ai_budget_exhausted" });
  });
});
