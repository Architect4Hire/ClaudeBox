var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();                 // persist data across restarts too

#pragma warning disable ASPIREPOSTGRES001 // WithPostgresMcp is experimental
var appdb = postgres.AddDatabase("appdb")
    .WithPostgresMcp(mcp => mcp
        // Pinned so .mcp.json can name a stable URL; Aspire would otherwise assign a random host port.
        .WithEndpoint("http", e => e.Port = 8765)
        // WithPostgresMcp hardcodes --access-mode=unrestricted. argparse takes the last flag, so this
        // wins and keeps MCP clients read-only. Appending (not replacing) keeps restricted mode even
        // if a future Aspire version stops passing its own flag.
        .WithArgs("--access-mode=restricted"));
#pragma warning restore ASPIREPOSTGRES001

// Local Redis container used as the distributed cache for read-through API caching.
var cache = builder.AddRedis("cache");

// Blob storage for user-uploaded files. RunAsEmulator pins this to an Azurite container, so it stays
// a local backing resource like postgres and cache — the Azure-shaped API is the emulator's surface,
// not a cloud dependency. RunAsEmulator also creates the container on startup, so nothing else has to.
var uploads = builder.AddAzureStorage("storage")
    .RunAsEmulator(azurite => azurite.WithDataVolume())  // persist blobs across restarts, as postgres does
    .AddBlobContainer("uploads");

// ASP.NET Core API. Gets its connections via service wiring (WithReference),
// and waits for its backing resources to be ready before it starts.
var api = builder.AddProject<Projects.RecipeBox_ApiService>("api")
    .WithReference(appdb)
    .WithReference(cache)
    .WithReference(uploads)
    .WaitFor(postgres)
    .WaitFor(cache)
    .WaitFor(uploads);

// Angular front end. Registered here so Aspire owns it. The API base URL is injected via service
// discovery (WithReference), never hardcoded.
builder.AddJavaScriptApp("web", "../web", runScriptName: "start")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();
