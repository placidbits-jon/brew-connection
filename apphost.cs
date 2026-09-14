#:package Aspire.Hosting.JavaScript@13.5.3
#:sdk Aspire.AppHost.Sdk@13.5.3
#:property AspireUseCliBundle=true
#:property NoWarn=ASPIREPERSISTENCE001

var builder = DistributedApplication.CreateBuilder(args);

const string arcadeDbPassword = "CoffeeDemo_Local_2026!";

var arcadeDb = builder.AddContainer("arcadedb", "arcadedata/arcadedb", "26.9.1")
    .WithEnvironment("ARCADEDB_SETTINGS",
        $"-Darcadedb.server.rootPassword={arcadeDbPassword} " +
        "-Darcadedb.server.plugins=Redis:com.arcadedb.redis.RedisProtocolPlugin," +
        "MongoDB:com.arcadedb.mongo.MongoDBProtocolPlugin," +
        "Postgres:com.arcadedb.postgres.PostgresProtocolPlugin," +
        "GremlinServer:com.arcadedb.server.gremlin.GremlinServerPlugin")
    .WithHttpEndpoint(targetPort: 2480, name: "http")
    .WithEndpoint(targetPort: 6379, name: "redis")
    .WithEndpoint(targetPort: 5432, name: "postgres")
    .WithEndpoint(targetPort: 27017, name: "mongo")
    .WithEndpoint(targetPort: 8182, name: "gremlin")
    .WithVolume("arcadedb-demo-data", "/home/arcadedb/databases")
    .WithPersistentLifetime()
    .WithExternalHttpEndpoints();

var ollama = builder.AddExecutable("local-models", "ollama", ".", "serve")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("OLLAMA_MODELS", Path.Combine(builder.AppHostDirectory, ".aspire", "models"))
    .WithEnvironment("OLLAMA_NO_CLOUD", "1")
    .WithHttpHealthCheck("/");
ollama.WithEnvironment("OLLAMA_HOST", ReferenceExpression.Create(
    $"127.0.0.1:{ollama.GetEndpoint("http").Property(EndpointProperty.TargetPort)}"));

var embedding = builder.AddProject("embedding", "./src/CoffeeCommunity.Embedding/CoffeeCommunity.Embedding.csproj")
    .WithEnvironment("Ollama__BaseUrl", ollama.GetEndpoint("http"))
    .WaitFor(ollama)
    .WithHttpHealthCheck("/health");

var api = builder.AddProject("api", "./src/CoffeeCommunity.Api/CoffeeCommunity.Api.csproj")
    .WithEnvironment("ArcadeDb__BaseUrl", arcadeDb.GetEndpoint("http"))
    .WithEnvironment("ArcadeDb__Password", arcadeDbPassword)
    .WithEnvironment("ArcadeDb__Database", "coffee_demo")
    .WithEnvironment("Embedding__BaseUrl", embedding.GetEndpoint("http"))
    .WithReference(embedding)
    .WaitFor(arcadeDb)
    .WaitFor(embedding)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.AddViteApp("web", "./src/coffee-community-web", "start")
    .WithEndpoint("http", endpoint => endpoint.Port = 4200)
    .WithEnvironment("API_HTTP", api.GetEndpoint("http"))
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.AddProject("telemetry-simulator", "./src/CoffeeCommunity.TelemetrySimulator/CoffeeCommunity.TelemetrySimulator.csproj")
    .WithEnvironment("Api__BaseUrl", api.GetEndpoint("http"))
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
