using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RecipeBox.ApiService.Data;
using RecipeBox.Tests.Infrastructure;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// The recipe domain's <see cref="AppApiFactory"/>: everything generic — the SQLite context, the
/// in-memory cache, <c>SeedAsync</c>/<c>QueryAsync</c> — comes from the base; this adds only what the
/// recipe slice itself needs, namely an in-memory stand-in for the blob-backed image store.
/// </summary>
public class RecipeApiFactory : AppApiFactory
{
    /// <summary>
    /// The image store the API writes to, exposed so a test can seed a blob or assert on what was
    /// stored — the bytes are otherwise only observable through the endpoint that serves them.
    /// </summary>
    public FakeRecipeImageStore Images { get; } = new();

    protected override void ConfigureDomainSettings(IWebHostBuilder builder)
    {
        // Never connected to — IRecipeImageStore is replaced below, so the BlobContainerClient this
        // configures is never resolved. It still has to parse: the Azure client integration hands the
        // value to DbConnectionStringBuilder at registration, so a Redis-style "host:port" dummy would
        // throw before any test ran. This form parses and names the container, and unlike an
        // "Endpoint=..." value it doesn't select the token-credential path.
        builder.UseSetting(
            "ConnectionStrings:uploads",
            "UseDevelopmentStorage=true;ContainerName=uploads");

        // The blob health check would resolve the real client, and /health is mapped in Development —
        // which is what WebApplicationFactory runs as. Nothing hits /health today; this keeps that from
        // becoming a trap for whoever adds the first test that does.
        builder.UseSetting("Aspire:Azure:Storage:Blobs:DisableHealthChecks", "true");
    }

    protected override void ConfigureDomainServices(IServiceCollection services)
    {
        // Swap the whole store rather than the BlobContainerClient underneath it: the seam exists
        // precisely so the layers above can be driven without an emulator. A singleton because the
        // instance is shared with the test asserting on it.
        services.RemoveAll<IRecipeImageStore>();
        services.AddSingleton<IRecipeImageStore>(Images);
    }
}
