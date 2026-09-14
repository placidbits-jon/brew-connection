namespace CoffeeCommunity.Api.Infrastructure;

public static class CommunityDiscoveryEndpoints
{
    public static void MapCommunityDiscovery(this WebApplication app)
    {
        var group = app.MapGroup("/api/demo/discover");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (GraphRequestException error) { return Results.Json(new { message = error.Message }, statusCode: error.Status); }
        });
        group.MapGet("", (string? query, string? mode, string? persona, string? type, string? syntax, string? similarTo, bool? availableOnly, CommunityDiscovery discovery, CancellationToken ct) =>
            discovery.Search(query ?? "blueberry", mode ?? "keyword", persona ?? "maya-chen", type ?? "all", syntax ?? "plain", similarTo, availableOnly ?? false, ct));
        group.MapPost("/ask", (DiscoveryQuestion request, CommunityDiscovery discovery, CancellationToken ct) => discovery.Ask(request, ct));
    }
}
