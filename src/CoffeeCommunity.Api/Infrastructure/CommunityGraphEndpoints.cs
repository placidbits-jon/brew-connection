namespace CoffeeCommunity.Api.Infrastructure;

public static class CommunityGraphEndpoints
{
    public static void MapCommunityGraph(this WebApplication app)
    {
        var group = app.MapGroup("/api/demo");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (GraphRequestException error) { return Results.Json(new { message = error.Message }, statusCode: error.Status); }
        });
        group.MapGet("/passport/{personSlug}", (string personSlug, CommunityGraph graph, CancellationToken ct) => graph.RunAsync(() => graph.Passport(personSlug, ct), false, ct));
        group.MapGet("/network/{personSlug}", (string personSlug, string? target, CommunityGraph graph, CancellationToken ct) => graph.RunAsync(() => graph.Network(personSlug, target ?? "luis-ortega", ct), false, ct));
        group.MapPost("/graph/meet", (MeetRequest request, CommunityGraph graph, CancellationToken ct) => graph.RunAsync(() => graph.Meet(request, ct), true, ct));
        group.MapPost("/graph/tastings", (BrewReactionRequest request, CommunityGraph graph, CancellationToken ct) => graph.RunAsync(() => graph.Reaction(request, "TASTED", ct), true, ct));
        group.MapPost("/graph/loves", (BrewReactionRequest request, CommunityGraph graph, CancellationToken ct) => graph.RunAsync(() => graph.Reaction(request, "LOVED", ct), true, ct));
        group.MapPost("/graph/reconnects", (ReconnectRequest request, CommunityGraph graph, CancellationToken ct) => graph.RunAsync(() => graph.Reconnect(request, ct), true, ct));
        group.MapPost("/graph/game-sessions", (GameSessionRequest request, CommunityGraph graph, CancellationToken ct) => graph.RunAsync(() => graph.Session(request, ct), true, ct));
        group.MapPost("/graph/game-results", (GameResultRequest request, CommunityGraph graph, CancellationToken ct) => graph.RunAsync(() => graph.Result(request, ct), true, ct));
    }
}
