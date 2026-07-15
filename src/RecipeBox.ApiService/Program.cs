using FluentValidation;
using RecipeBox.ApiService.Data;
using RecipeBox.ApiService.Business;
using RecipeBox.ApiService.Facade;
using RecipeBox.ApiService.Managers.Infrastructure;
using RecipeBox.ApiService.Managers.Validators;

var builder = WebApplication.CreateBuilder(args);

// Aspire cross-cutting concerns: telemetry, health checks, resilience, service discovery.
builder.AddServiceDefaults();

// EF Core context via the Aspire Npgsql integration, keyed to the "recipesdb" AppHost resource.
// The connection string is injected by Aspire (WithReference) — never hardcoded here.
builder.AddNpgsqlDbContext<RecipeDbContext>("recipesdb");

// Distributed cache via the Aspire Redis integration, keyed to the "cache" AppHost resource.
// Registers IDistributedCache; the connection is injected, not configured by hand.
builder.AddRedisDistributedCache("cache");

// Recipes feature: layered Controller → Facade → Business → Data (each on the interface below it).
builder.Services.AddScoped<IRecipeRepository, RecipeRepository>();
builder.Services.AddScoped<IRecipeBusiness, RecipeBusiness>();
builder.Services.AddScoped<IRecipeFacade, RecipeFacade>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateRecipeViewModelValidator>();

// Shared error shape: ProblemDetails, with domain/validation exceptions mapped centrally.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddControllers();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

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
