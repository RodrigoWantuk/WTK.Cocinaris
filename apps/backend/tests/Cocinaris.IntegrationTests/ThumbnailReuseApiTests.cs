using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Ai.Images;
using Cocinaris.Modules.Recipes.Thumbnails;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0055 Phase 3: a similar dish reuses a stored image instead of generating one.</summary>
public sealed partial class RecipeExecutionApiTests
{
    [Fact]
    public async Task ASimilarDishReusesTheImageAndADifferentOneGeneratesItsOwn()
    {
        var storage = Path.Combine(Path.GetTempPath(), "cocinaris-reuse-" + Guid.NewGuid().ToString("n"));
        var provider = new FakeImageProvider(1024, 576);
        await using var host = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Ai:Images:Enabled", "true");
            builder.UseSetting("Ai:Images:StoragePath", storage);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IImageGenerationProvider>(provider);
                services.Remove(services.Single(item => item.ServiceType == typeof(IHostedService) && item.ImplementationType == typeof(Cocinaris.Api.Recipes.RecipeThumbnailWorker)));
            });
        });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Test-Subject", "reuse-owner");
        await CsrfAsync(client);

        var reworded = Visual
            .Replace("\"white rice\",\"chicken breast in tomato sauce\"", "\"cooked chicken breast with tomato\",\"rice\"")
            .Replace("Chicken in tomato sauce with white rice.", "Tomato chicken over rice.");
        var fish = Visual.Replace("chicken breast in tomato sauce", "white fish fillet in tomato sauce").Replace("fully cooked chicken", "flaky fish");
        var first = await SeedVisualRecipeAsync(host, "reuse-owner", "Frango ao molho");
        var similar = await SeedVisualRecipeAsync(host, "reuse-owner", "Frango com tomate", reworded);
        var different = await SeedVisualRecipeAsync(host, "reuse-owner", "Peixe ao molho", fish);
        foreach (var recipe in new[] { first, similar, different })
        {
            await client.GetFromJsonAsync<JsonElement>($"/api/v1/recipes/{recipe}");
        }

        using (var scope = host.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<RecipeThumbnailService>();
            while (await service.ProcessNextAsync(CancellationToken.None))
            {
            }

            var assets = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().RecipeThumbnails.AsNoTracking().ToListAsync();
            Assert.Equal(3, assets.Count);
            Assert.All(assets, asset => Assert.Equal("Ready", asset.State));
            var reused = Assert.Single(assets, asset => asset.SourceRenderKey is not null);
            Assert.True(reused.ReuseScore >= 0.8);
            Assert.Contains(assets, asset => asset.RenderKey == reused.SourceRenderKey);
        }

        // Two images were generated (the first dish and the fish); the similar dish cost nothing.
        Assert.Equal(2, provider.Calls);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/recipes/{similar}");
        Assert.Equal("ready", detail.GetProperty("thumbnail").GetProperty("state").GetString());
        var image = await client.GetAsync(detail.GetProperty("thumbnail").GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/webp", image.Content.Headers.ContentType!.MediaType);
        Directory.Delete(storage, recursive: true);
    }
}
