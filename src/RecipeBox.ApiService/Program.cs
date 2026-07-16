using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RecipeBox.ApiService.Data;
using RecipeBox.ApiService.Business;
using RecipeBox.ApiService.Facade;
using RecipeBox.ApiService.Managers.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Aspire cross-cutting concerns: telemetry, health checks, resilience, service discovery.
builder.AddServiceDefaults();

// EF Core context via the Aspire Npgsql integration, keyed to the "appdb" AppHost resource.
// The connection string is injected by Aspire (WithReference) — never hardcoded here.
builder.AddNpgsqlDbContext<AppDbContext>("appdb");

// Distributed cache via the Aspire Redis integration, keyed to the "cache" AppHost resource.
// Registers IDistributedCache; the connection is injected, not configured by hand.
builder.AddRedisDistributedCache("cache");

// Blob container for recipe images via the Aspire Azure Storage integration, keyed to the
// "uploads" AppHost resource (Azurite locally). Registers BlobContainerClient; as with the two
// above, the connection is injected by Aspire, never a connection string here.
builder.AddAzureBlobContainerClient("uploads");

// Recipes feature: layered Controller → Facade → Business → DataLayer → Repository (each on the
// interface below it). The image store sits beside the repository as the data layer's second store:
// the repository owns the recipe row, this owns the image bytes.
builder.Services.AddScoped<IRecipeImageStore, BlobRecipeImageStore>();
builder.Services.AddScoped<IRecipeRepository, RecipeRepository>();
builder.Services.AddScoped<IRecipeDataLayer, RecipeDataLayer>();
builder.Services.AddScoped<IRecipeBusiness, RecipeBusiness>();
builder.Services.AddScoped<IRecipeFacade, RecipeFacade>();
// Every validator in this assembly, keyed off Program rather than a specific validator — adding a
// validator never means editing this line.
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Shared error shape: ProblemDetails, with domain/validation exceptions mapped centrally.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddControllers();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// In Development, apply pending EF migrations on startup so a fresh `aspire run` comes up with a
// ready schema — no manual `dotnet ef database update` needed for local orchestration. Guarded to
// the Npgsql provider so the in-memory SQLite used by the endpoint tests is never migrated with the
// Postgres-specific migrations.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsNpgsql())
    {
        await db.Database.MigrateAsync();
        // Give a fresh database a handful of recipes, with their photographs, so the list renders on
        // first run. No-op once any exist. The image store is resolved here rather than inside the
        // seeder so the seeder stays a plain function of the two stores it writes to.
        await RecipeSeeder.SeedAsync(
            db,
            scope.ServiceProvider.GetRequiredService<IRecipeImageStore>(),
            scope.ServiceProvider.GetRequiredService<ILogger<Program>>());
    }
}

// Aspire health endpoints (/health, /alive).
app.MapDefaultEndpoints();

app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();

// Exposed so the endpoint integration tests can drive the app via WebApplicationFactory.
public partial class Program;
