using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Recipes.Thumbnails;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Cocinaris.Modules.Ai.Images;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0030: recipe thumbnails through the HTTP boundary against PostgreSQL, with the fake image provider.</summary>
public sealed partial class RecipeExecutionApiTests
{
    private const string Visual = """
        {"schemaVersion":"1","appearanceDescription":"Chicken in tomato sauce with white rice.","visibleComponents":["white rice","chicken breast in tomato sauce"],"dishFormat":"plated_main_with_sides","plating":"single plate","sauceAppearance":"red tomato sauce","textureAndDoneness":["fully cooked chicken"],"garnish":[],"dominantColors":["red","white"],"excludedElements":["cutlery","text"]}
        """;

    [Fact]
    public async Task AThumbnailIsQueuedOnReadGeneratedOnceAndServedOnlyToTheOwner()
    {
        var storage = Path.Combine(Path.GetTempPath(), "cocinaris-thumbnails-" + Guid.NewGuid().ToString("n"));
        await using var host = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Ai:Images:Enabled", "true");
            builder.UseSetting("Ai:Images:StoragePath", storage);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IImageGenerationProvider>(new FakeImageProvider(1024, 576));
                // The test runs the job itself, so the background worker must not race it.
                var worker = services.Single(item => item.ServiceType == typeof(IHostedService) && item.ImplementationType == typeof(Cocinaris.Api.Recipes.RecipeThumbnailWorker));
                services.Remove(worker);
            });
        });
        HttpClient ClientOf(string subject)
        {
            var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
            return client;
        }

        using var owner = ClientOf("thumbnail-owner");
        using var other = ClientOf("thumbnail-other");
        await CsrfAsync(owner);
        await CsrfAsync(other);
        var first = await SeedVisualRecipeAsync(host, "thumbnail-owner", "Frango ao molho");
        // A second recipe with the same appearance written differently shares the image.
        var second = await SeedVisualRecipeAsync(host, "thumbnail-owner", "Frango de novo", Visual.Replace("\"white rice\",\"chicken breast in tomato sauce\"", "\"Chicken breast in tomato sauce\", \" White  rice \""));

        var pending = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/recipes/{first}");
        Assert.Equal("pending", pending.GetProperty("thumbnail").GetProperty("state").GetString());
        var list = await owner.GetFromJsonAsync<JsonElement>("/api/v1/recipes");
        Assert.All(list.EnumerateArray(), item => Assert.Equal("pending", item.GetProperty("thumbnail").GetProperty("state").GetString()));

        using (var scope = host.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<RecipeThumbnailService>();
            Assert.True(await service.ProcessNextAsync(CancellationToken.None));
            Assert.False(await service.ProcessNextAsync(CancellationToken.None));
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var asset = Assert.Single(await database.RecipeThumbnails.AsNoTracking().ToListAsync());
            Assert.Equal(("Ready", 1024, 576, "image/png"), (asset.State, asset.Width!.Value, asset.Height!.Value, asset.MediaType!));
            var ledger = Assert.Single(await database.AiUsageLedgerEntries.AsNoTracking().Where(item => item.Operation == AiOperationRegistry.ThumbnailGenerate).ToListAsync());
            Assert.Equal("Settled", ledger.Status);
        }

        var ready = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/recipes/{second}");
        var url = ready.GetProperty("thumbnail").GetProperty("url").GetString()!;
        Assert.Equal("ready", ready.GetProperty("thumbnail").GetProperty("state").GetString());
        var image = await owner.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.Contains("private", image.Headers.CacheControl!.ToString());
        var bytes = await image.Content.ReadAsByteArrayAsync();
        Assert.Equal(0x89, bytes[0]);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(url)).StatusCode);
        Directory.Delete(storage, recursive: true);
    }

    private static async Task<Guid> SeedVisualRecipeAsync(WebApplicationFactory<Program> host, string subject, string name, string visual = Visual)
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var owner = await database.Users.Where(user => user.Subject == subject).Select(user => user.Id).SingleAsync();
        var recipeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        database.Recipes.Add(new RecipeRecord { Id = recipeId, OwnerUserId = owner, CurrentRevisionNumber = 1, CreatedAt = now, UpdatedAt = now });
        database.RecipeRevisions.Add(new RecipeRevisionRecord
        {
            Id = Guid.NewGuid(),
            RecipeId = recipeId,
            OwnerUserId = owner,
            RevisionNumber = 1,
            Name = name,
            MealTypesJson = "[\"dinner\"]",
            Servings = 2,
            NormalizedRecipeJson = "{\"recipe\":{\"ingredients\":[]}}",
            ThumbnailVisualJson = visual,
            SourceCandidateId = "cand-1",
            SourceGenerationSessionId = Guid.NewGuid(),
            CreatedAt = now
        });
        await database.SaveChangesAsync();
        return recipeId;
    }
}
