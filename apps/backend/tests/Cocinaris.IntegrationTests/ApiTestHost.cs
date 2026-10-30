using System.Security.Claims;
using System.Text.Encodings.Web;
using Cocinaris.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cocinaris.IntegrationTests;

/// <summary>API host on a test PostgreSQL database; the <c>X-Test-Subject</c> header selects the signed-in user.</summary>
internal sealed class CocinarisFactory(string connectionString) : WebApplicationFactory<Program>
{
    public async Task EnsureDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Cocinaris", connectionString);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
            services.AddAuthentication(TestAuthenticationHandler.TestScheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.TestScheme, _ => { });
        });
        // Tests run the ingredient resolution job themselves; the background worker would race them
        // (and query its tables before EnsureDatabaseAsync has migrated).
        builder.ConfigureTestServices(services =>
        {
            foreach (var worker in services.Where(item => item.ServiceType == typeof(IHostedService)
                && (item.ImplementationType == typeof(Cocinaris.Api.Ingredients.IngredientResolutionWorker) || item.ImplementationType == typeof(Cocinaris.Api.Inventory.InventoryAutoRemovalWorker))).ToList())
            {
                services.Remove(worker);
            }
        });
    }
}

internal sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string TestScheme = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subject = Request.Headers["X-Test-Subject"].FirstOrDefault() ?? "integration-user";
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject), new Claim("iss", "https://integration.test")], TestScheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
    }
}
