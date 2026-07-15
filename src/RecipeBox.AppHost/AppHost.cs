var builder = DistributedApplication.CreateBuilder(args);

// Local PostgreSQL container + the "recipesdb" database.
var postgres = builder.AddPostgres("postgres");
var recipesdb = postgres.AddDatabase("recipesdb");

// Local Redis container used as the distributed cache for read-through API caching.
var cache = builder.AddRedis("cache");

// ASP.NET Core API. Gets its connections via service wiring (WithReference),
// and waits for its backing resources to be ready before it starts.
var api = builder.AddProject<Projects.RecipeBox_ApiService>("api")
    .WithReference(recipesdb)
    .WithReference(cache)
    .WaitFor(postgres)
    .WaitFor(cache);

// Angular front end. Registered here so Aspire owns it; the actual app is scaffolded later
// (Prompt 3). The API base URL is injected via service discovery (WithReference), never hardcoded.
builder.AddJavaScriptApp("web", "../RecipeBox", runScriptName: "start")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();
